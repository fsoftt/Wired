using Wired.Core.Playback;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public class SleepTimerTests : IDisposable
{
    private readonly FakeAudioPlayer _player = new();
    private readonly PlaybackService _playback;
    private readonly FakeTime _time = new();
    private readonly SleepTimer _timer;

    public SleepTimerTests()
    {
        _playback = TestPlayback.Create(_player);
        _timer = new SleepTimer(_playback, _time);
    }

    public void Dispose() => _timer.Dispose();

    [Fact]
    public async Task Remaining_counts_down()
    {
        await _playback.PlayAsync([TestPlayback.Item("a"), TestPlayback.Item("b")]);

        _timer.Start(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(1), _timer.Remaining);

        _time.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(40), _timer.Remaining);
    }

    [Fact]
    public async Task Volume_stays_full_until_the_last_ten_seconds_then_fades()
    {
        await _playback.PlayAsync([TestPlayback.Item("a")]);
        _timer.Start(TimeSpan.FromSeconds(60));

        _time.Advance(TimeSpan.FromSeconds(50));
        Assert.Equal(1f, _player.Volume);

        _time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(0.5, _player.Volume, 3);

        _time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(0.1, _player.Volume, 3);
        Assert.True(_player.IsPlaying);
    }

    [Fact]
    public async Task At_the_deadline_playback_pauses_volume_is_restored_and_timer_ends()
    {
        await _playback.PlayAsync([TestPlayback.Item("a")]);
        var changes = 0;
        _timer.Changed += (_, _) => changes++;
        _timer.Start(TimeSpan.FromSeconds(30));

        _time.Advance(TimeSpan.FromSeconds(25));
        Assert.True(_player.IsPlaying);
        Assert.Equal(0.5, _player.Volume, 3);
        var changesBeforeDeadline = changes;

        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.False(_player.IsPlaying);
        Assert.Equal(1f, _player.Volume);
        Assert.False(_timer.IsActive);
        Assert.Null(_timer.Remaining);
        Assert.True(changes > changesBeforeDeadline);
    }

    [Fact]
    public async Task Cancel_mid_fade_restores_volume_and_nothing_pauses_later()
    {
        await _playback.PlayAsync([TestPlayback.Item("a")]);
        _timer.Start(TimeSpan.FromSeconds(30));
        _time.Advance(TimeSpan.FromSeconds(25));
        Assert.Equal(0.5, _player.Volume, 3);

        _timer.Cancel();

        Assert.Equal(1f, _player.Volume);
        Assert.False(_timer.IsActive);

        _time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(_player.IsPlaying);
        Assert.Equal(1f, _player.Volume);
    }

    [Fact]
    public async Task Starting_again_replaces_the_previous_timer()
    {
        await _playback.PlayAsync([TestPlayback.Item("a")]);
        _timer.Start(TimeSpan.FromMinutes(10));
        _time.Advance(TimeSpan.FromMinutes(1));

        _timer.Start(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(2), _timer.Remaining);

        _time.Advance(TimeSpan.FromSeconds(90));
        Assert.True(_player.IsPlaying);
        Assert.Equal(TimeSpan.FromSeconds(30), _timer.Remaining);

        _time.Advance(TimeSpan.FromSeconds(30));
        Assert.False(_player.IsPlaying);
        Assert.False(_timer.IsActive);
    }

    [Fact]
    public async Task End_of_track_stays_paused_on_the_next_item()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        await _playback.PlayAsync([a, b]);
        var changes = 0;
        _timer.Changed += (_, _) => changes++;

        _timer.StartEndOfTrack();
        Assert.True(_timer.IsActive);
        Assert.True(_timer.IsEndOfTrack);
        Assert.Null(_timer.Remaining);
        Assert.True(_playback.StopAfterCurrent);

        var changesBeforeEnd = changes;
        _player.FinishCurrentTrack();

        Assert.False(_player.IsPlaying);
        Assert.Equal(b, _playback.CurrentItem);
        Assert.False(_timer.IsActive);
        Assert.False(_playback.StopAfterCurrent);
        Assert.True(changes > changesBeforeEnd);
    }

    [Fact]
    public async Task Toggle_after_end_of_track_plays_the_next_track_from_the_start()
    {
        var b = TestPlayback.Item("b");
        await _playback.PlayAsync([TestPlayback.Item("a"), b]);
        _timer.StartEndOfTrack();
        _player.FinishCurrentTrack();

        _playback.TogglePlayPause();

        await TestWait.UntilAsync(() => _playback.IsPlaying);
        Assert.Equal(b, _playback.CurrentItem);
        Assert.Equal(b.LocalTrack!.FilePath, _player.LoadedFile);
        Assert.Equal(TimeSpan.Zero, _player.Position);
    }

    [Fact]
    public async Task Cancel_of_end_of_track_clears_stop_after_current()
    {
        await _playback.PlayAsync([TestPlayback.Item("a"), TestPlayback.Item("b")]);
        _timer.StartEndOfTrack();

        _timer.Cancel();

        Assert.False(_playback.StopAfterCurrent);
        Assert.False(_timer.IsActive);
        Assert.False(_timer.IsEndOfTrack);

        _player.FinishCurrentTrack();
        Assert.True(_player.IsPlaying);
        Assert.Equal("b", _playback.CurrentItem!.Title);
    }

    [Fact]
    public void Start_with_zero_duration_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _timer.Start(TimeSpan.Zero));
        Assert.False(_timer.IsActive);
    }

    /// <summary>Manual clock: Advance moves time forward and fires every timer period that elapses, in order.</summary>
    private sealed class FakeTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        private readonly List<FakeTimer> _timers = [];

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new FakeTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            var target = _now + by;
            while (true)
            {
                var next = _timers
                    .Where(t => t.Due is not null && t.Due <= target)
                    .OrderBy(t => t.Due)
                    .FirstOrDefault();
                if (next is null)
                    break;

                _now = next.Due!.Value;
                next.Fire();
            }

            _now = target;
        }

        private sealed class FakeTimer(FakeTime owner, TimerCallback callback, object? state) : ITimer
        {
            private TimeSpan _period;

            public DateTimeOffset? Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = owner.GetUtcNow() + dueTime;
                _period = period;
                return true;
            }

            public void Fire()
            {
                // Reschedule before invoking, so a callback that disposes the timer wins.
                Due = _period > TimeSpan.Zero ? Due + _period : null;
                callback(state);
            }

            public void Dispose() => Due = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
