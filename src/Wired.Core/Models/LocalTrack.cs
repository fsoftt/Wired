namespace Wired.Core.Models;

/// <summary>
/// A track stored on the device and available for offline playback.
/// </summary>
/// <param name="ArtworkPath">Cover saved on the device, so it shows offline.</param>
/// <param name="IsEnriched">Cover saved and tags written into the audio file.</param>
/// <param name="LoudnessDb">Measured loudness of the audio (see <see cref="Audio.LoudnessMeter"/>); null until measured.</param>
public sealed record LocalTrack(
    Guid Id,
    string Title,
    string Artist,
    TimeSpan? Duration,
    string? ThumbnailUrl,
    string FilePath,
    string SourceId,
    string RemoteId,
    DateTimeOffset AddedAt,
    string? Album = null,
    string? ArtworkPath = null,
    bool IsEnriched = false,
    double? LoudnessDb = null)
{
    /// <summary>Cover to show: the copy stored on the device when there is one, else the remote thumbnail.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Artwork => ArtworkPath is not null && File.Exists(ArtworkPath) ? ArtworkPath : ThumbnailUrl;

    public bool IsSameAs(RemoteTrack remote) =>
        SourceId == remote.SourceId && RemoteId == remote.RemoteId;
}
