using TagLib;
using Wired.Core.Abstractions;

namespace Wired.Infrastructure.Media;

/// <summary>Reads and writes MP4 (.m4a) and Matroska (.mka) tags with TagLib#.</summary>
public sealed class TagLibAudioTagger : IAudioTagger
{
    public AudioTags? Read(string filePath)
    {
        using var file = TagLib.File.Create(filePath);
        var tag = file.Tag;

        // Encoders add their own tags ("Lavc…"): a file only counts as tagged with real metadata.
        if (string.IsNullOrWhiteSpace(tag.Title) && tag.Performers.Length == 0 && string.IsNullOrWhiteSpace(tag.Album) && tag.Pictures.Length == 0)
            return null;

        var cover = tag.Pictures.FirstOrDefault(p => p.Type == PictureType.FrontCover) ?? tag.Pictures.FirstOrDefault();
        return new AudioTags(
            NullIfEmpty(tag.Title),
            tag.Performers.Where(p => !string.IsNullOrWhiteSpace(p)).ToList(),
            IsMatroska(filePath) ? null : NullIfEmpty(tag.Album), // Same TITLE field as the song in Matroska.
            file.Properties?.Duration is { TotalSeconds: > 0 } duration ? duration : null,
            cover?.Data?.Data);
    }

    public TimeSpan? ReadDuration(string filePath)
    {
        using var file = TagLib.File.Create(filePath);
        return file.Properties?.Duration is { TotalSeconds: > 0 } duration ? duration : null;
    }

    public void Write(string filePath, string title, IReadOnlyList<string> artists, string? album, byte[]? cover)
    {
        using var file = TagLib.File.Create(filePath);
        var tag = file.Tag;

        tag.Title = title;
        tag.Performers = [.. artists];
        tag.AlbumArtists = artists.Count > 0 ? [artists[0]] : [];

        // TagLib# stores Matroska's album and track titles in the same TITLE field, so writing the
        // album there would overwrite the song title: Matroska files keep title, artists and cover only.
        if (album is not null && !IsMatroska(filePath))
            tag.Album = album;

        if (cover is { Length: > 0 })
        {
            tag.Pictures =
            [
                new Picture(new ByteVector(cover))
                {
                    Type = PictureType.FrontCover,
                    MimeType = IsWebp(cover) ? "image/webp" : IsPng(cover) ? "image/png" : "image/jpeg",
                    Description = "Cover",
                },
            ];
        }

        file.Save();
    }

    private static bool IsMatroska(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".mka" or ".mkv" or ".webm";

    private static bool IsPng(byte[] data) => data.Length > 4 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

    private static bool IsWebp(byte[] data) =>
        data.Length > 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P';

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
