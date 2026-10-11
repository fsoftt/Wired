using System.Globalization;
using System.Text;
using Wired.Core.Models;

namespace Wired.Core.Storage;

public readonly record struct TrackKey(string SourceId, string RemoteId);

/// <summary>A playlist as stored in a shared M3U file.</summary>
/// <param name="Key">Playlist id ("N" format) or <see cref="M3uPlaylistFormat.LikesKey"/>.</param>
/// <param name="IsDeleted">Tombstone for a playlist whose older file could not be removed.</param>
public sealed record PlaylistDocument(string Key, string Name, bool IsDeleted, IReadOnlyList<TrackKey> Tracks);

/// <summary>
/// Extended M3U that any music player understands, plus a "#WIRED:" line so Wired can map it back
/// to a playlist. Entries point to the track files ("../Artist - Title [source-id].m4a").
/// </summary>
public static class M3uPlaylistFormat
{
    public const string LikesKey = "likes";
    public const string Extension = ".m3u";
    private const string WiredTag = "#WIRED:";
    private const string DeletedSuffix = ":deleted";

    public static string Write(string key, string name, IReadOnlyList<LocalTrack> tracks)
    {
        var text = new StringBuilder()
            .AppendLine("#EXTM3U")
            .AppendLine($"#PLAYLIST:{name}")
            .AppendLine($"{WiredTag}{key}");

        foreach (var track in tracks)
        {
            var seconds = track.Duration is { } duration ? (int)duration.TotalSeconds : -1;
            text.AppendLine(CultureInfo.InvariantCulture, $"#EXTINF:{seconds},{track.Artist} - {track.Title}")
                .AppendLine($"../{FileNameOf(track)}");
        }

        return text.ToString();
    }

    public static string WriteTombstone(string key, string name) =>
        $"#EXTM3U\n#PLAYLIST:{name}\n{WiredTag}{key}{DeletedSuffix}\n";

    public static bool TryRead(string text, out PlaylistDocument document)
    {
        string? key = null, name = null;
        var deleted = false;
        var tracks = new List<TrackKey>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith(WiredTag, StringComparison.Ordinal))
            {
                key = line[WiredTag.Length..];
                deleted = key.EndsWith(DeletedSuffix, StringComparison.Ordinal);
                if (deleted)
                    key = key[..^DeletedSuffix.Length];
            }
            else if (line.StartsWith("#PLAYLIST:", StringComparison.Ordinal))
            {
                name = line["#PLAYLIST:".Length..].Trim();
            }
            else if (line.Length > 0 && !line.StartsWith('#')
                     && MusicFileNaming.TryParse(Path.GetFileName(line.Replace('\\', '/')), out var parsed))
            {
                tracks.Add(new TrackKey(parsed.SourceId, parsed.RemoteId));
            }
        }

        document = new PlaylistDocument(key ?? string.Empty, name ?? string.Empty, deleted, tracks);
        return key is { Length: > 0 } && name is { Length: > 0 };
    }

    private static string FileNameOf(LocalTrack track)
    {
        var current = Path.GetFileName(track.FilePath);
        return MusicFileNaming.TryParse(current, out _) ? current : MusicFileNaming.FileNameFor(track);
    }
}
