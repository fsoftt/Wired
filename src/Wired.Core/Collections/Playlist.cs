namespace Wired.Core.Collections;

/// <summary>
/// A user defined, ordered list of tracks from the library.
/// </summary>
public sealed record Playlist(Guid Id, string Name, IReadOnlyList<Guid> TrackIds, DateTimeOffset CreatedAt)
{
    public const int MaxNameLength = 60;

    public bool Contains(Guid trackId) => TrackIds.Contains(trackId);
}
