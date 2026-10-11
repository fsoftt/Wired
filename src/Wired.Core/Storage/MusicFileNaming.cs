using System.Text.RegularExpressions;
using Wired.Core.Models;

namespace Wired.Core.Storage;

/// <summary>
/// File names that carry enough metadata to rebuild the library from the music folder alone
/// (e.g. after reinstalling the app): "Artist - Title [source-remoteId].ext".
/// </summary>
public static partial class MusicFileNaming
{
    private const int MaxNamePartLength = 80;

    public static string FileNameFor(string title, string artist, string sourceId, string remoteId, string extension) =>
        $"{Sanitize(artist)} - {Sanitize(title)} [{sourceId}-{remoteId}]{NormalizeExtension(extension)}";

    public static string FileNameFor(LocalTrack track) =>
        FileNameFor(track.Title, track.Artist, track.SourceId, track.RemoteId, Path.GetExtension(track.FilePath));

    /// <summary>Reads back what <see cref="FileNameFor(string,string,string,string,string)"/> wrote.</summary>
    public static bool TryParse(string fileName, out ParsedMusicFileName parsed)
    {
        var match = Pattern().Match(fileName);
        parsed = match.Success
            ? new ParsedMusicFileName(match.Groups["artist"].Value, match.Groups["title"].Value, match.Groups["source"].Value, match.Groups["id"].Value)
            : default;
        return match.Success;
    }

    /// <summary>Makes <paramref name="value"/> safe to use inside a file name.</summary>
    public static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|', '[', ']']).ToHashSet();
        var clean = new string(value.Select(c => invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        clean = Whitespace().Replace(clean, " ").Trim().TrimEnd('.');

        if (clean.Length > MaxNamePartLength)
            clean = clean[..MaxNamePartLength].Trim();

        return clean.Length == 0 ? "Desconocido" : clean;
    }

    /// <summary>
    /// Shared music folders only accept files whose extension maps to an audio MIME type
    /// (Android rejects ".mp4"/".webm" in Music/ because they mean video). Audio-only streams use
    /// those containers, so they get the audio flavor of the same container.
    /// </summary>
    private static readonly Dictionary<string, string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp4"] = ".m4a",
        [".webm"] = ".mka",
    };

    private static string NormalizeExtension(string extension)
    {
        if (extension.Length == 0)
            return string.Empty;

        var normalized = extension.StartsWith('.') ? extension : $".{extension}";
        return AudioExtensions.GetValueOrDefault(normalized, normalized).ToLowerInvariant();
    }

    // Artist " - " Title " [" source "-" id "]" — the source id never contains "-", the remote id may.
    [GeneratedRegex(@"^(?<artist>.+?) - (?<title>.+) \[(?<source>[a-z0-9]+)-(?<id>[^\]]+)\](\.\w+)?$")]
    private static partial Regex Pattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Whitespace();
}

public readonly record struct ParsedMusicFileName(string Artist, string Title, string SourceId, string RemoteId);
