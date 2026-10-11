using Wired.Core.Models;

namespace Wired.Core.Playback;

public static class PlaybackServiceExtensions
{
    public static Task PlayAsync(this IPlaybackService playback, IEnumerable<LocalTrack> tracks, int startIndex = 0) =>
        playback.PlayAsync(tracks.Select(PlaybackItem.FromLocal).ToList(), startIndex);

    public static Task ShuffleAsync(this IPlaybackService playback, IEnumerable<LocalTrack> tracks) =>
        playback.ShuffleAsync(tracks.Select(PlaybackItem.FromLocal).ToList());
}
