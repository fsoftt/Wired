using Wired.Core.Abstractions;
using Wired.Core.Audio;

namespace Wired.Core.Playback;

public sealed class PlaybackService : IPlaybackService
{
    /// <summary>"Previous" restarts the current track when it has played longer than this.</summary>
    public static readonly TimeSpan RestartThreshold = TimeSpan.FromSeconds(3);

    private readonly IAudioPlayer _player;
    private readonly PlaybackQueue _queue;
    private readonly IPlaybackSourceResolver _sources;

    /// <summary>Incremented on every load so a slow stream does not start after the user skipped it.</summary>
    private int _loadVersion;

    /// <summary>A restored queue whose current item is not opened yet, and where to resume it.</summary>
    private bool _needsLoad;
    private PlaybackItem? _resumeItem;
    private TimeSpan _resumePosition;
    private bool _stopAfterCurrent;

    public PlaybackService(IAudioPlayer player, PlaybackQueue queue, IPlaybackSourceResolver sources)
    {
        _player = player;
        _queue = queue;
        _sources = sources;
        _player.PlaybackEnded += OnPlaybackEnded;
        _player.StateChangedExternally += (_, _) => RaiseStateChanged();
    }

    public event EventHandler? StateChanged;

    public event EventHandler<PlaybackFailedEventArgs>? PlaybackFailed;

    public PlaybackItem? CurrentItem => _queue.Current;

    /// <summary>Snapshot: the queue may change while the caller reads it.</summary>
    public IReadOnlyList<PlaybackItem> Queue => [.. _queue.Items];

    public int CurrentIndex => _queue.CurrentIndex;

    public bool IsPlaying => _player.IsPlaying;

    public bool IsLoading { get; private set; }

    public bool HasNext => _queue.HasNext;

    public bool HasPrevious => _queue.HasPrevious;

    public bool IsShuffleEnabled => _queue.IsShuffled;

    public RepeatMode RepeatMode => _queue.Repeat;

    public TimeSpan Position => _needsLoad ? _resumePosition : _player.Position;

    public TimeSpan Duration => !_needsLoad && _player.Duration > TimeSpan.Zero
        ? _player.Duration
        : CurrentItem?.Duration ?? TimeSpan.Zero;

    public IReadOnlyList<int> QueueOriginalOrder => _queue.OriginalOrder;

    public float Volume
    {
        get => _player.Volume;
        set => _player.Volume = Math.Clamp(value, 0f, 1f);
    }

    public bool StopAfterCurrent
    {
        get => _stopAfterCurrent;
        set
        {
            if (_stopAfterCurrent == value)
                return;

            _stopAfterCurrent = value;
            RaiseStateChanged();
        }
    }

    public void Restore(IReadOnlyList<PlaybackItem> queue, IReadOnlyList<int> originalOrder, int currentIndex, TimeSpan position, bool shuffle, RepeatMode repeat)
    {
        if (CurrentItem is not null || queue.Count == 0)
            return;

        var original = originalOrder.Count == queue.Count && originalOrder.All(i => i >= 0 && i < queue.Count) && originalOrder.Distinct().Count() == queue.Count
            ? originalOrder.Select(i => queue[i]).ToList()
            : queue;

        _queue.Restore(queue, original, currentIndex, shuffle, repeat);
        _needsLoad = true;
        _resumeItem = _queue.Current;
        _resumePosition = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        RaiseStateChanged();
    }

    public Task PlayAsync(IReadOnlyList<PlaybackItem> items, int startIndex = 0, CancellationToken cancellationToken = default)
    {
        _queue.Load(items, startIndex);
        return PlayCurrentAsync(cancellationToken);
    }

    public Task ShuffleAsync(IReadOnlyList<PlaybackItem> items, CancellationToken cancellationToken = default)
    {
        _queue.LoadShuffled(items);
        return PlayCurrentAsync(cancellationToken);
    }

    public Task PlayNextAsync(IReadOnlyList<PlaybackItem> items, CancellationToken cancellationToken = default) =>
        EnqueueAsync(() => _queue.InsertNext(items), cancellationToken);

    public Task AddToQueueAsync(IReadOnlyList<PlaybackItem> items, CancellationToken cancellationToken = default) =>
        EnqueueAsync(() => _queue.Append(items), cancellationToken);

    public void MoveInQueue(int from, int to)
    {
        _queue.Move(from, to);
        RaiseStateChanged();
    }

    public async Task RemoveFromQueueAsync(int index, CancellationToken cancellationToken = default)
    {
        if (!_queue.RemoveAt(index))
        {
            RaiseStateChanged();
            return;
        }

        // The playing entry was removed: continue with the one that took its place
        // (a restored queue that was never started stays paused).
        if (_needsLoad)
        {
            _resumePosition = TimeSpan.Zero;
            _needsLoad = _queue.Current is not null;
            RaiseStateChanged();
            return;
        }

        _player.Stop();
        if (_queue.Current is not null)
            await PlayCurrentAsync(cancellationToken);
        else
            RaiseStateChanged();
    }

    public async Task SkipToAsync(int index, CancellationToken cancellationToken = default)
    {
        if (_queue.MoveTo(index))
            await PlayCurrentAsync(cancellationToken);
    }

    /// <summary>When nothing is playing, queuing starts playback: that is what the user expects.</summary>
    private async Task EnqueueAsync(Action enqueue, CancellationToken cancellationToken)
    {
        var wasIdle = CurrentItem is null;
        enqueue();

        if (wasIdle)
            await PlayCurrentAsync(cancellationToken);
        else
            RaiseStateChanged();
    }

    public void TogglePlayPause()
    {
        if (CurrentItem is null || IsLoading)
            return;

        if (_needsLoad)
        {
            _ = PlayCurrentAsync(CancellationToken.None);
            return;
        }

        if (_player.IsPlaying)
            _player.Pause();
        else
            _player.Play();

        RaiseStateChanged();
    }

    public void Pause()
    {
        if (!_player.IsPlaying)
            return;

        _player.Pause();
        RaiseStateChanged();
    }

    public async Task NextAsync(CancellationToken cancellationToken = default)
    {
        if (_queue.MoveNext())
            await PlayCurrentAsync(cancellationToken);
    }

    public async Task PreviousAsync(CancellationToken cancellationToken = default)
    {
        if (Position > RestartThreshold || !_queue.MovePrevious())
        {
            Seek(TimeSpan.Zero);
            return;
        }

        await PlayCurrentAsync(cancellationToken);
    }

    public void Seek(TimeSpan position)
    {
        if (_needsLoad)
            _resumePosition = position;
        else
            _player.Seek(position);
        RaiseStateChanged();
    }

    public void SetShuffle(bool enabled)
    {
        _queue.SetShuffle(enabled);
        RaiseStateChanged();
    }

    public void SetRepeatMode(RepeatMode mode)
    {
        _queue.Repeat = mode;
        RaiseStateChanged();
    }

    public void RefreshTrack(Models.LocalTrack track)
    {
        var wasResumeItem = _resumeItem?.LocalTrack?.Id == track.Id && ReferenceEquals(_resumeItem, _queue.Current);
        if (!_queue.Replace(track))
            return;

        // The entry waiting to resume is now a new instance: keep its saved position.
        if (wasResumeItem)
            _resumeItem = _queue.Current;
        RaiseStateChanged();
    }

    public async Task ForgetAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        var wasCurrent = CurrentItem?.Id == itemId;
        _queue.Remove(itemId);

        if (!wasCurrent)
            return;

        if (_needsLoad)
        {
            _resumePosition = TimeSpan.Zero;
            _needsLoad = _queue.Current is not null;
            RaiseStateChanged();
            return;
        }

        _player.Stop();
        if (_queue.Current is not null)
            await PlayCurrentAsync(cancellationToken);
        else
            RaiseStateChanged();
    }

    private async Task PlayCurrentAsync(CancellationToken cancellationToken)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        var item = _queue.Current;

        // A restored queue resumes where it was left; any other change of track starts from the beginning.
        var resumeAt = _needsLoad && ReferenceEquals(item, _resumeItem) ? _resumePosition : TimeSpan.Zero;
        _needsLoad = false;
        _resumeItem = null;

        _player.Stop();
        if (item is null)
        {
            RaiseStateChanged();
            return;
        }

        IsLoading = true;
        RaiseStateChanged();

        try
        {
            var source = await _sources.ResolveAsync(item, cancellationToken);
            if (version != _loadVersion)
            {
                // The user skipped while the stream was opening. Disposing may touch the network.
                if (source is StreamPlaybackSource skipped)
                    _ = Task.Run(skipped.Content.Dispose);
                return;
            }

            await _player.LoadAsync(source, cancellationToken);
            if (version != _loadVersion)
                return;

            _player.GainDb = LoudnessNormalization.GainFor(item.LocalTrack?.LoudnessDb);
            if (resumeAt > TimeSpan.Zero)
                _player.Seek(resumeAt);
            _player.Play();
        }
        catch (Exception error) when (version == _loadVersion)
        {
            _player.Stop();
            PlaybackFailed?.Invoke(this, new PlaybackFailedEventArgs(item, error));
        }
        catch (Exception)
        {
            // A newer item is already loading; this failure is irrelevant.
            return;
        }
        finally
        {
            if (version == _loadVersion)
                IsLoading = false;
        }

        RaiseStateChanged();
    }

    private async void OnPlaybackEnded(object? sender, EventArgs e)
    {
        if (_stopAfterCurrent)
        {
            // Sleep timer: stay paused on the next track, ready to continue tomorrow.
            _stopAfterCurrent = false;
            if (_queue.MoveNext())
            {
                _player.Stop();
                _needsLoad = true;
                _resumePosition = TimeSpan.Zero;
            }

            RaiseStateChanged();
            return;
        }

        if (_queue.Repeat == RepeatMode.One && CurrentItem is not null)
        {
            _player.Seek(TimeSpan.Zero);
            _player.Play();
            RaiseStateChanged();
            return;
        }

        if (_queue.MoveNext())
            await PlayCurrentAsync(CancellationToken.None);
        else
            RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
