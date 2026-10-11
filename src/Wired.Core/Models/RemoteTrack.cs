namespace Wired.Core.Models;

/// <summary>
/// A track found by a music source plugin that has not been downloaded yet.
/// </summary>
/// <param name="SourceId">Id of the plugin that produced this result.</param>
/// <param name="RemoteId">Identifier of the track inside its source (e.g. the video or song id).</param>
public sealed record RemoteTrack(
    string SourceId,
    string RemoteId,
    string Title,
    string Artist,
    TimeSpan? Duration,
    string? ThumbnailUrl);
