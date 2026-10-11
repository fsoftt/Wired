using Wired.Core.Playback;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public class PlaybackServiceTests
{
    private readonly FakeAudioPlayer _player = new();
    private readonly PlaybackService _service;

    public PlaybackServiceTests() => _service = TestPlayback.Create(_player);

    [Fact]
    public async Task Plays_next_item_when_current_one_ends()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        await _service.PlayAsync([a, b]);

        _player.FinishCurrentTrack();

        Assert.Equal(b, _service.CurrentItem);
        Assert.Equal(b.LocalTrack!.FilePath, _player.LoadedFile);
        Assert.True(_service.IsPlaying);
    }

    [Fact]
    public async Task Repeat_one_loops_the_current_item()
    {
        var a = TestPlayback.Item("a");
        await _service.PlayAsync([a, TestPlayback.Item("b")]);
        _service.SetRepeatMode(RepeatMode.One);
        _player.Position = TimeSpan.FromMinutes(2);

        _player.FinishCurrentTrack();

        Assert.Equal(a, _service.CurrentItem);
        Assert.Equal(TimeSpan.Zero, _player.Position);
        Assert.True(_service.IsPlaying);
    }

    [Fact]
    public async Task Repeat_all_starts_over_at_the_end_of_the_queue()
    {
        var a = TestPlayback.Item("a");
        await _service.PlayAsync([a, TestPlayback.Item("b")], startIndex: 1);
        _service.SetRepeatMode(RepeatMode.All);

        _player.FinishCurrentTrack();

        Assert.Equal(a, _service.CurrentItem);
    }

    [Fact]
    public async Task Streams_remote_items_through_their_plugin()
    {
        var remote = PlaybackItem.FromStream(TestData.RemoteTrack("xyz"));

        await _service.PlayAsync([remote]);

        Assert.Equal("stream:xyz", _player.LoadedStream);
        Assert.Null(_player.LoadedFile);
        Assert.True(_service.CurrentItem!.IsStream);
        Assert.False(_service.IsLoading);
    }

    [Fact]
    public async Task Failures_are_reported_instead_of_thrown()
    {
        _player.LoadError = new IOException("broken");
        PlaybackFailedEventArgs? failure = null;
        _service.PlaybackFailed += (_, e) => failure = e;

        await _service.PlayAsync([TestPlayback.Item("a")]);

        Assert.Equal("broken", failure?.Error.Message);
        Assert.False(_service.IsPlaying);
        Assert.False(_service.IsLoading);
    }

    [Fact]
    public async Task Previous_restarts_track_when_it_has_played_for_a_while()
    {
        var b = TestPlayback.Item("b");
        await _service.PlayAsync([TestPlayback.Item("a"), b], startIndex: 1);
        _player.Position = TimeSpan.FromSeconds(30);

        await _service.PreviousAsync();

        Assert.Equal(b, _service.CurrentItem);
        Assert.Equal(TimeSpan.Zero, _player.Position);
    }

    [Fact]
    public async Task Forgetting_the_current_item_plays_the_next_one()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        await _service.PlayAsync([a, b]);

        await _service.ForgetAsync(a.Id);

        Assert.Equal(b, _service.CurrentItem);
        Assert.True(_service.IsPlaying);
    }

    [Fact]
    public async Task Toggle_pauses_and_resumes()
    {
        await _service.PlayAsync([TestPlayback.Item("a")]);

        _service.TogglePlayPause();
        Assert.False(_service.IsPlaying);

        _service.TogglePlayPause();
        Assert.True(_service.IsPlaying);
    }

    [Fact]
    public async Task System_interruptions_are_reported_as_state_changes()
    {
        await _service.PlayAsync([TestPlayback.Item("a")]);
        var raised = false;
        _service.StateChanged += (_, _) => raised = true;

        _player.Interrupt();

        Assert.True(raised);
        Assert.False(_service.IsPlaying);
    }
}
