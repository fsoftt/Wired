namespace Wired.Core.Playback;

/// <summary>
/// High level playback: a queue of items played through an <see cref="Abstractions.IAudioPlayer"/>.
/// Errors never escape: they are reported through <see cref="PlaybackFailed"/>.
/// </summary>
public interface IPlaybackService
{
    PlaybackItem? CurrentItem { get; }

    /// <summary>The whole queue in play order.</summary>
    IReadOnlyList<PlaybackItem> Queue { get; }

    /// <summary>Position of <see cref="CurrentItem"/> in <see cref="Queue"/>, or -1.</summary>
    int CurrentIndex { get; }

    bool IsPlaying { get; }

    /// <summary>True while a track is being prepared (e.g. connecting to a stream).</summary>
    bool IsLoading { get; }

    bool HasNext { get; }

    bool HasPrevious { get; }

    bool IsShuffleEnabled { get; }

    RepeatMode RepeatMode { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    /// <summary>The order shuffle returns to, as positions in <see cref="Queue"/>.</summary>
    IReadOnlyList<int> QueueOriginalOrder { get; }

    /// <summary>Output level from 0 to 1 (fades); songs are evened out on top of it.</summary>
    float Volume { get; set; }

    /// <summary>When set, playback pauses at the end of the current track (sleep timer) and the flag clears.</summary>
    bool StopAfterCurrent { get; set; }

    /// <summary>Raised when the current item, playing state or modes change. May be raised from any thread.</summary>
    event EventHandler? StateChanged;

    /// <summary>Raised when an item could not be played. May be raised from any thread.</summary>
    event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    Task PlayAsync(IReadOnlyList<PlaybackItem> items, int startIndex = 0, CancellationToken cancellationToken = default);

    /// <summary>Turns shuffle on and plays <paramref name="items"/> in random order.</summary>
    Task ShuffleAsync(IReadOnlyList<PlaybackItem> items, CancellationToken cancellationToken = default);

    /// <summary>Queues <paramref name="items"/> right after the current one (starts playing if idle).</summary>
    Task PlayNextAsync(IReadOnlyList<PlaybackItem> items, CancellationToken cancellationToken = default);

    /// <summary>Adds <paramref name="items"/> at the end of the queue (starts playing if idle).</summary>
    Task AddToQueueAsync(IReadOnlyList<PlaybackItem> items, CancellationToken cancellationToken = default);

    void MoveInQueue(int from, int to);

    Task RemoveFromQueueAsync(int index, CancellationToken cancellationToken = default);

    Task SkipToAsync(int index, CancellationToken cancellationToken = default);

    void TogglePlayPause();

    void Pause();

    Task NextAsync(CancellationToken cancellationToken = default);

    Task PreviousAsync(CancellationToken cancellationToken = default);

    void Seek(TimeSpan position);

    void SetShuffle(bool enabled);

    void SetRepeatMode(RepeatMode mode);

    /// <summary>
    /// Puts back a queue saved when the app was closed, paused at <paramref name="position"/>.
    /// Nothing is opened until the user presses play. Ignored if something is already queued.
    /// </summary>
    void Restore(IReadOnlyList<PlaybackItem> queue, IReadOnlyList<int> originalOrder, int currentIndex, TimeSpan position, bool shuffle, RepeatMode repeat);

    /// <summary>A downloaded track changed (cover, title...): show its new version in the queue.</summary>
    void RefreshTrack(Models.LocalTrack track);

    /// <summary>Removes the item from the queue, moving on if it is playing.</summary>
    Task ForgetAsync(Guid itemId, CancellationToken cancellationToken = default);
}

public sealed class PlaybackFailedEventArgs(PlaybackItem item, Exception error) : EventArgs
{
    public PlaybackItem Item { get; } = item;

    public Exception Error { get; } = error;
}
