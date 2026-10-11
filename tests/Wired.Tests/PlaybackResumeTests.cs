using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Infrastructure.Library;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class PlaybackResumeTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly InMemoryLibrary _library = new();
    private readonly FakeAudioPlayer _player = new();
    private readonly PlaybackService _playback;

    public PlaybackResumeTests() => _playback = TestPlayback.Create(_player);

    public void Dispose() => _paths.Dispose();

    // ----- PlaybackService: restored queue --------------------------------------------------

    [Fact]
    public async Task Restored_queue_stays_paused_until_play_is_pressed()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");

        _playback.Restore([a, b], [0, 1], currentIndex: 1, TimeSpan.FromMinutes(1), shuffle: false, RepeatMode.Off);

        Assert.Equal(b, _playback.CurrentItem);
        Assert.False(_playback.IsPlaying);
        Assert.Null(_player.LoadedFile);
        Assert.Equal(TimeSpan.FromMinutes(1), _playback.Position);

        _playback.TogglePlayPause();
        await TestWait.UntilAsync(() => _player.IsPlaying);

        Assert.Equal(b.LocalTrack!.FilePath, _player.LoadedFile);
        Assert.Equal(TimeSpan.FromMinutes(1), _player.Position);
    }

    [Fact]
    public async Task Seek_before_loading_changes_the_position_it_resumes_from()
    {
        var a = TestPlayback.Item("a");
        _playback.Restore([a], [0], 0, TimeSpan.FromMinutes(1), false, RepeatMode.Off);

        _playback.Seek(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromMinutes(5), _playback.Position);
        Assert.Null(_player.LoadedFile);
        Assert.False(_player.IsPlaying);

        _playback.TogglePlayPause();
        await TestWait.UntilAsync(() => _player.IsPlaying);

        Assert.Equal(TimeSpan.FromMinutes(5), _player.Position);
    }

    [Fact]
    public void Negative_restored_position_is_treated_as_the_start()
    {
        _playback.Restore([TestPlayback.Item("a")], [0], 0, TimeSpan.FromSeconds(-5), false, RepeatMode.Off);

        Assert.Equal(TimeSpan.Zero, _playback.Position);
    }

    [Fact]
    public async Task Restore_is_ignored_when_something_is_already_queued()
    {
        var a = TestPlayback.Item("a");
        await _playback.PlayAsync([a]);

        _playback.Restore([TestPlayback.Item("b")], [0], 0, TimeSpan.FromMinutes(1), false, RepeatMode.Off);

        Assert.Equal(a, _playback.CurrentItem);
        Assert.Single(_playback.Queue);
    }

    [Fact]
    public void Restored_shuffled_queue_returns_to_the_original_order_when_shuffle_is_turned_off()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        var c = TestPlayback.Item("c");

        // Play order is c, a, b; the order shuffle returns to is a, b, c.
        _playback.Restore([c, a, b], [1, 2, 0], 0, TimeSpan.Zero, shuffle: true, RepeatMode.All);

        Assert.True(_playback.IsShuffleEnabled);
        Assert.Equal(RepeatMode.All, _playback.RepeatMode);
        Assert.Equal(new[] {1, 2, 0}, _playback.QueueOriginalOrder);

        _playback.SetShuffle(false);

        Assert.Equal(new[] {"a", "b", "c"}, _playback.Queue.Select(i => i.Title));
        Assert.Equal(c, _playback.CurrentItem);
        Assert.Equal(2, _playback.CurrentIndex);
    }

    [Fact]
    public async Task Removing_the_current_entry_of_a_restored_queue_keeps_it_paused()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        var c = TestPlayback.Item("c");
        _playback.Restore([a, b, c], [0, 1, 2], 0, TimeSpan.FromMinutes(1), false, RepeatMode.Off);

        await _playback.RemoveFromQueueAsync(0);

        Assert.Equal(b, _playback.CurrentItem);
        Assert.False(_playback.IsPlaying);
        Assert.Null(_player.LoadedFile);
        Assert.Equal(TimeSpan.Zero, _playback.Position);

        _playback.TogglePlayPause();
        await TestWait.UntilAsync(() => _player.IsPlaying);

        Assert.Equal(b.LocalTrack!.FilePath, _player.LoadedFile);
    }

    [Fact]
    public async Task Forgetting_the_current_entry_of_a_restored_queue_keeps_it_paused()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        _playback.Restore([a, b], [0, 1], 0, TimeSpan.FromMinutes(1), false, RepeatMode.Off);

        await _playback.ForgetAsync(a.Id);

        Assert.Equal(b, _playback.CurrentItem);
        Assert.False(_playback.IsPlaying);
        Assert.Null(_player.LoadedFile);
        Assert.Equal(TimeSpan.Zero, _playback.Position);
    }

    [Fact]
    public async Task Removing_an_entry_before_the_current_one_keeps_the_saved_position()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        _playback.Restore([a, b], [0, 1], 1, TimeSpan.FromMinutes(1), false, RepeatMode.Off);

        await _playback.RemoveFromQueueAsync(0);

        Assert.Equal(b, _playback.CurrentItem);
        Assert.Equal(TimeSpan.FromMinutes(1), _playback.Position);
        Assert.False(_playback.IsPlaying);
    }

    // ----- PlaybackQueue: restore and shuffle -----------------------------------------------

    [Fact]
    public void Queue_restore_keeps_the_order_that_shuffle_returns_to()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        var c = TestPlayback.Item("c");
        var queue = new PlaybackQueue();

        queue.Restore([c, a, b], [a, b, c], currentIndex: 1, shuffled: true, RepeatMode.One);

        Assert.Equal(new[] {1, 2, 0}, queue.OriginalOrder);
        Assert.Equal("a", queue.Current!.Title);

        queue.SetShuffle(false);

        Assert.Equal(new[] {"a", "b", "c"}, queue.Items.Select(i => i.Title));
        Assert.Equal("a", queue.Current!.Title);
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Fact]
    public void Shuffle_on_then_off_returns_to_the_original_order_and_keeps_the_current_item()
    {
        var queue = new PlaybackQueue(new Random(1));
        queue.Load(new[] { "a", "b", "c", "d" }.Select(TestPlayback.Item).ToList(), startIndex: 2);

        queue.SetShuffle(true);
        queue.SetShuffle(false);

        Assert.Equal(new[] {"a", "b", "c", "d"}, queue.Items.Select(i => i.Title));
        Assert.Equal("c", queue.Current!.Title);
        Assert.Equal(2, queue.CurrentIndex);
        Assert.Equal(new[] {0, 1, 2, 3}, queue.OriginalOrder);
    }

    // ----- PlaybackStatePersister: snapshot and restore -------------------------------------

    [Fact]
    public async Task Snapshot_keeps_the_saved_position_before_the_track_is_loaded()
    {
        _playback.Restore([TestPlayback.Item("a")], [0], 0, TimeSpan.FromMinutes(1), false, RepeatMode.Off);

        var snapshot = new PlaybackStatePersister(_playback, new JsonPlaybackStateStore(_paths), _library).TakeSnapshot();

        Assert.Equal(TimeSpan.FromMinutes(1), snapshot.Position);
        Assert.Null(_player.LoadedFile);
    }

    [Fact]
    public async Task Local_tracks_are_restored_by_library_id_after_an_app_restart()
    {
        var a = AddToLibrary("a");
        var b = AddToLibrary("b");
        var c = AddToLibrary("c");
        await _playback.PlayAsync([PlaybackItem.FromLocal(a), PlaybackItem.FromLocal(b), PlaybackItem.FromLocal(c)], startIndex: 1);
        _player.Position = TimeSpan.FromSeconds(42);
        await SaveSessionAsync(_playback);

        var (restarted, player) = await RestartAsync();

        Assert.Equal(new[] {"a", "b", "c"}, restarted.Queue.Select(i => i.Title));
        Assert.Equal(1, restarted.CurrentIndex);
        Assert.Equal(TimeSpan.FromSeconds(42), restarted.Position);
        Assert.False(restarted.IsPlaying);

        restarted.TogglePlayPause();
        await TestWait.UntilAsync(() => player.IsPlaying);

        Assert.Equal(b.FilePath, player.LoadedFile);
        Assert.Equal(TimeSpan.FromSeconds(42), player.Position);
    }

    [Fact]
    public async Task Streamed_items_are_restored_and_play_through_their_plugin()
    {
        var a = AddToLibrary("a");
        var remote = TestData.RemoteTrack("xyz");
        await _playback.PlayAsync([PlaybackItem.FromLocal(a), PlaybackItem.FromStream(remote)], startIndex: 1);
        _player.Position = TimeSpan.FromSeconds(7);
        await SaveSessionAsync(_playback);

        var (restarted, player) = await RestartAsync();

        var restored = restarted.CurrentItem!;
        Assert.True(restored.IsStream);
        Assert.Equal(remote, restored.RemoteTrack);
        Assert.Equal(TimeSpan.FromSeconds(7), restarted.Position);

        restarted.TogglePlayPause();
        await TestWait.UntilAsync(() => player.IsPlaying);

        Assert.Equal("stream:xyz", player.LoadedStream);
        Assert.Equal(TimeSpan.FromSeconds(7), player.Position);
    }

    [Fact]
    public async Task Deleted_current_track_is_skipped_and_playback_moves_to_the_next_one_from_the_start()
    {
        var a = AddToLibrary("a");
        var b = AddToLibrary("b");
        var c = AddToLibrary("c");
        await _playback.PlayAsync([PlaybackItem.FromLocal(a), PlaybackItem.FromLocal(b), PlaybackItem.FromLocal(c)], startIndex: 1);
        _player.Position = TimeSpan.FromSeconds(30);
        await SaveSessionAsync(_playback);
        _library.Tracks.Remove(b);

        var (restarted, _) = await RestartAsync();

        Assert.Equal(new[] {"a", "c"}, restarted.Queue.Select(i => i.Title));
        Assert.Equal("c", restarted.CurrentItem!.Title);
        Assert.Equal(1, restarted.CurrentIndex);
        Assert.Equal(TimeSpan.Zero, restarted.Position);
    }

    [Fact]
    public async Task Deleted_track_before_the_current_one_remaps_the_index_and_keeps_the_position()
    {
        var a = AddToLibrary("a");
        var b = AddToLibrary("b");
        var c = AddToLibrary("c");
        await _playback.PlayAsync([PlaybackItem.FromLocal(a), PlaybackItem.FromLocal(b), PlaybackItem.FromLocal(c)], startIndex: 2);
        _player.Position = TimeSpan.FromSeconds(20);
        await SaveSessionAsync(_playback);
        _library.Tracks.Remove(a);

        var (restarted, player) = await RestartAsync();

        Assert.Equal(new[] {"b", "c"}, restarted.Queue.Select(i => i.Title));
        Assert.Equal(1, restarted.CurrentIndex);
        Assert.Equal("c", restarted.CurrentItem!.Title);
        Assert.Equal(TimeSpan.FromSeconds(20), restarted.Position);

        restarted.TogglePlayPause();
        await TestWait.UntilAsync(() => player.IsPlaying);

        Assert.Equal(c.FilePath, player.LoadedFile);
        Assert.Equal(TimeSpan.FromSeconds(20), player.Position);
    }

    [Fact]
    public async Task Deleted_last_track_being_current_moves_back_to_the_previous_remaining_track()
    {
        var a = AddToLibrary("a");
        var b = AddToLibrary("b");
        var c = AddToLibrary("c");
        await _playback.PlayAsync([PlaybackItem.FromLocal(a), PlaybackItem.FromLocal(b), PlaybackItem.FromLocal(c)], startIndex: 2);
        _player.Position = TimeSpan.FromSeconds(20);
        await SaveSessionAsync(_playback);
        _library.Tracks.Remove(c);

        var (restarted, _) = await RestartAsync();

        Assert.Equal(new[] {"a", "b"}, restarted.Queue.Select(i => i.Title));
        Assert.Equal("b", restarted.CurrentItem!.Title);
        Assert.Equal(1, restarted.CurrentIndex);
        Assert.Equal(TimeSpan.Zero, restarted.Position);
    }

    [Fact]
    public async Task Shuffle_and_repeat_are_restored_with_the_order_shuffle_returns_to()
    {
        var a = AddToLibrary("a");
        var b = AddToLibrary("b");
        var c = AddToLibrary("c");
        await _playback.PlayAsync([PlaybackItem.FromLocal(a), PlaybackItem.FromLocal(b), PlaybackItem.FromLocal(c)]);
        _playback.SetShuffle(true);
        _playback.SetRepeatMode(RepeatMode.All);
        await SaveSessionAsync(_playback);

        var (restarted, _) = await RestartAsync();

        Assert.True(restarted.IsShuffleEnabled);
        Assert.Equal(RepeatMode.All, restarted.RepeatMode);
        Assert.Equal(_playback.Queue.Select(i => i.Title), restarted.Queue.Select(i => i.Title));
        Assert.Equal(_playback.CurrentIndex, restarted.CurrentIndex);
        Assert.Equal(_playback.QueueOriginalOrder, restarted.QueueOriginalOrder);

        restarted.SetShuffle(false);

        Assert.Equal(new[] {"a", "b", "c"}, restarted.Queue.Select(i => i.Title));
        Assert.Equal(_playback.CurrentItem!.Title, restarted.CurrentItem!.Title);
    }

    [Fact]
    public async Task Deleted_track_is_also_removed_from_the_order_shuffle_returns_to()
    {
        var a = AddToLibrary("a");
        var b = AddToLibrary("b");
        var c = AddToLibrary("c");
        await _playback.PlayAsync([PlaybackItem.FromLocal(a), PlaybackItem.FromLocal(b), PlaybackItem.FromLocal(c)]);
        _playback.SetShuffle(true);
        await SaveSessionAsync(_playback);
        _library.Tracks.Remove(b);

        var (restarted, _) = await RestartAsync();
        restarted.SetShuffle(false);

        Assert.Equal(new[] {"a", "c"}, restarted.Queue.Select(i => i.Title));
    }

    // ----- JsonPlaybackStateStore -----------------------------------------------------------

    [Fact]
    public async Task Snapshot_written_by_one_store_instance_is_read_by_a_new_instance()
    {
        var snapshot = new PlaybackSnapshot(
            [new PlaybackSnapshotEntry(Guid.NewGuid(), null), new PlaybackSnapshotEntry(null, TestData.RemoteTrack("xyz"))],
            [1, 0],
            1,
            TimeSpan.FromSeconds(73),
            Shuffle: true,
            RepeatMode.One);

        await new JsonPlaybackStateStore(_paths).SaveAsync(snapshot);
        var loaded = await new JsonPlaybackStateStore(_paths).LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Entries, loaded.Entries);
        Assert.Equal(snapshot.OriginalOrder, loaded.OriginalOrder);
        Assert.Equal(snapshot.CurrentIndex, loaded.CurrentIndex);
        Assert.Equal(snapshot.Position, loaded.Position);
        Assert.True(loaded.Shuffle);
        Assert.Equal(RepeatMode.One, loaded.Repeat);
    }

    // ----- Known bugs -----------------------------------------------------------------------

    [Fact]
    public async Task Previous_on_a_restored_track_does_not_carry_its_saved_position_into_the_previous_track()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        _playback.Restore([a, b], [0, 1], currentIndex: 1, TimeSpan.FromSeconds(2), false, RepeatMode.Off);

        await _playback.PreviousAsync();

        Assert.Equal(a, _playback.CurrentItem);
        Assert.Equal(TimeSpan.Zero, _player.Position);
        Assert.Equal(TimeSpan.Zero, _playback.Position);
    }

    [Fact]
    public async Task Previous_restarts_the_first_restored_track_from_the_start()
    {
        var a = TestPlayback.Item("a");
        _playback.Restore([a], [0], 0, TimeSpan.FromMinutes(2), false, RepeatMode.Off);

        await _playback.PreviousAsync();

        Assert.Equal(a, _playback.CurrentItem);
        Assert.Equal(TimeSpan.Zero, _playback.Position);
    }

    [Fact]
    public async Task Forgetting_a_track_queued_twice_removes_every_copy_from_the_play_order()
    {
        var track = TestData.LocalTrack("x");
        var first = PlaybackItem.FromLocal(track);
        var second = PlaybackItem.FromLocal(track);
        var other = TestPlayback.Item("b");
        await _playback.PlayAsync([first, other, second], startIndex: 1);

        await _playback.ForgetAsync(track.Id);

        Assert.DoesNotContain(_playback.Queue, i => i.Id == track.Id);
    }

    // ----- Helpers --------------------------------------------------------------------------

    private LocalTrack AddToLibrary(string title)
    {
        var track = TestData.LocalTrack(title);
        _library.Tracks.Add(track);
        return track;
    }

    /// <summary>Saves the session the way the app does on exit: a snapshot written to the JSON file.</summary>
    private async Task SaveSessionAsync(PlaybackService playback)
    {
        var store = new JsonPlaybackStateStore(_paths);
        await store.SaveAsync(new PlaybackStatePersister(playback, store, _library).TakeSnapshot());
    }

    /// <summary>Starts a new app instance that restores the saved session from disk.</summary>
    private async Task<(PlaybackService Playback, FakeAudioPlayer Player)> RestartAsync()
    {
        var player = new FakeAudioPlayer();
        var playback = TestPlayback.Create(player);
        await new PlaybackStatePersister(playback, new JsonPlaybackStateStore(_paths), _library).RestoreAsync();
        return (playback, player);
    }
}
