namespace Wired.Core.Playback;

/// <summary>
/// Pauses playback after a while or at the end of the current track. The last seconds fade out
/// so the music does not cut off abruptly.
/// </summary>
public sealed class SleepTimer : IDisposable
{
    public static readonly TimeSpan FadeOut = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly IPlaybackService _playback;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private ITimer? _timer;
    private DateTimeOffset? _deadline;

    public SleepTimer(IPlaybackService playback, TimeProvider? time = null)
    {
        _playback = playback;
        _time = time ?? TimeProvider.System;
        _playback.StateChanged += OnPlaybackStateChanged;
    }

    /// <summary>Raised when the timer starts, ticks or ends. May be raised from any thread.</summary>
    public event EventHandler? Changed;

    public bool IsActive => _deadline is not null || IsEndOfTrack;

    /// <summary>Waiting for the current track to end instead of a time.</summary>
    public bool IsEndOfTrack { get; private set; }

    /// <summary>Time left, or null when off or waiting for the end of the track.</summary>
    public TimeSpan? Remaining
    {
        get
        {
            var deadline = _deadline;
            if (deadline is null)
                return null;

            var left = deadline.Value - _time.GetUtcNow();
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    public void Start(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        lock (_gate)
        {
            StopCore();
            _deadline = _time.GetUtcNow() + duration;
            _timer = _time.CreateTimer(_ => OnTick(), null, Tick, Tick);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void StartEndOfTrack()
    {
        lock (_gate)
        {
            StopCore();
            IsEndOfTrack = true;
            _playback.StopAfterCurrent = true;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Cancel()
    {
        lock (_gate)
            StopCore();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _playback.StateChanged -= OnPlaybackStateChanged;
        lock (_gate)
            StopCore();
    }

    private void OnTick()
    {
        bool expired;
        lock (_gate)
        {
            if (Remaining is not { } left)
                return;

            expired = left <= TimeSpan.Zero;
            if (expired)
            {
                _playback.Pause();
                StopCore(); // Paused already: the next play is at full volume.
            }
            else if (left < FadeOut)
            {
                _playback.Volume = (float)(left / FadeOut);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The end-of-track stop happened (or the user turned it off from elsewhere).</summary>
    private void OnPlaybackStateChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (!IsEndOfTrack || _playback.StopAfterCurrent)
                return;

            IsEndOfTrack = false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void StopCore()
    {
        _timer?.Dispose();
        _timer = null;
        _deadline = null;

        if (IsEndOfTrack)
        {
            IsEndOfTrack = false;
            _playback.StopAfterCurrent = false;
        }

        _playback.Volume = 1;
    }
}
