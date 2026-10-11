namespace Wired.Core.Abstractions;

/// <summary>Metadata embedded in an audio file (MP4/Matroska tags).</summary>
public sealed record AudioTags(string? Title, IReadOnlyList<string> Artists, string? Album, TimeSpan? Duration, byte[]? Cover);

/// <summary>
/// Reads and writes tags inside audio files, so other players show proper names and covers and a
/// reinstall can rebuild the library with them.
/// </summary>
public interface IAudioTagger
{
    AudioTags? Read(string filePath);

    /// <summary>Length of the audio, also for files without tags.</summary>
    TimeSpan? ReadDuration(string filePath) => Read(filePath)?.Duration;

    void Write(string filePath, string title, IReadOnlyList<string> artists, string? album, byte[]? cover);
}
