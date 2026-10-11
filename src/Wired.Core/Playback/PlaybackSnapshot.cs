using Wired.Core.Models;

namespace Wired.Core.Playback;

/// <summary>The play queue as it was when the app closed, so it can be resumed.</summary>
/// <param name="Entries">Play order.</param>
/// <param name="OriginalOrder">Order shuffle returns to, as positions in <paramref name="Entries"/>.</param>
public sealed record PlaybackSnapshot(
    IReadOnlyList<PlaybackSnapshotEntry> Entries,
    IReadOnlyList<int> OriginalOrder,
    int CurrentIndex,
    TimeSpan Position,
    bool Shuffle,
    RepeatMode Repeat);

/// <summary>A downloaded track (by library id) or a streamed one.</summary>
public sealed record PlaybackSnapshotEntry(Guid? LocalTrackId, RemoteTrack? Remote);

public interface IPlaybackStateStore
{
    Task<PlaybackSnapshot?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken = default);
}
