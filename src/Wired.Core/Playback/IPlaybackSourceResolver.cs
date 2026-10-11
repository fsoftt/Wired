namespace Wired.Core.Playback;

/// <summary>
/// Turns a queue item into something the audio player can open.
/// </summary>
public interface IPlaybackSourceResolver
{
    Task<PlaybackSource> ResolveAsync(PlaybackItem item, CancellationToken cancellationToken = default);
}
