using Wired.Core.Playback;

namespace Wired.Core.Enrichment;

/// <summary>
/// Covers on demand: the song that starts playing gets its cover right away instead of waiting
/// for its turn in the library backfill. Every improved track also reaches the play queue, so
/// "Now playing", the mini player and the notification show it.
/// </summary>
public sealed class PlayingTrackArtwork(IPlaybackService playback, LibraryEnrichmentService enrichment)
{
    private Guid? _requested;

    public void Start()
    {
        enrichment.TrackUpdated += (_, track) => playback.RefreshTrack(track);
        playback.StateChanged += (_, _) => OnPlaybackChanged();
        OnPlaybackChanged();
    }

    private void OnPlaybackChanged()
    {
        if (playback.CurrentItem?.LocalTrack is not { } track || track.ArtworkPath is { } path && File.Exists(path))
            return;

        // Once per track: a failure is reported, not retried on every state change.
        if (_requested == track.Id)
            return;
        _requested = track.Id;

        _ = Task.Run(() => enrichment.EnsureCoverAsync(track.Id));
    }
}
