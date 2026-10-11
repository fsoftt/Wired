using Wired.Core.Playback;
using Wired.Presentation.Services;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public class QueueOperationsTests
{
    private static List<PlaybackItem> Items(params string[] titles) => titles.Select(TestPlayback.Item).ToList();

    [Fact]
    public void InsertNext_on_an_empty_queue_loads_the_items_and_starts_at_the_first()
    {
        var queue = new PlaybackQueue();
        var inserted = Items("a", "b");

        queue.InsertNext(inserted);

        Assert.Equal(0, queue.CurrentIndex);
        Assert.Same(inserted[0], queue.Current);
        Assert.Equal(2, queue.Items.Count);
    }

    [Fact]
    public void InsertNext_places_items_right_after_the_current_one()
    {
        var items = Items("a", "b", "c");
        var queue = new PlaybackQueue();
        queue.Load(items, startIndex: 0);
        var x = TestPlayback.Item("x");
        var y = TestPlayback.Item("y");

        queue.InsertNext([x, y]);

        Assert.Equal(new[] { items[0], x, y, items[1], items[2] }, queue.Items);
        Assert.Same(items[0], queue.Current);
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Fact]
    public void Append_adds_at_the_end_without_changing_the_current_item()
    {
        var items = Items("a", "b");
        var queue = new PlaybackQueue();
        queue.Load(items, startIndex: 1);
        var x = TestPlayback.Item("x");

        queue.Append([x]);

        Assert.Equal(new[] { items[0], items[1], x }, queue.Items);
        Assert.Same(items[1], queue.Current);
        Assert.Equal(1, queue.CurrentIndex);
    }

    [Fact]
    public void Moving_an_item_across_the_current_one_keeps_the_same_current_instance()
    {
        var items = Items("a", "b", "c", "d");
        var queue = new PlaybackQueue();
        queue.Load(items, startIndex: 1);

        queue.Move(3, 0);
        Assert.Same(items[1], queue.Current);
        Assert.Equal(2, queue.CurrentIndex);

        queue.Move(0, 3);
        Assert.Same(items[1], queue.Current);
        Assert.Equal(1, queue.CurrentIndex);
    }

    [Fact]
    public void Moving_the_current_item_updates_CurrentIndex()
    {
        var items = Items("a", "b", "c", "d");
        var queue = new PlaybackQueue();
        queue.Load(items, startIndex: 1);

        queue.Move(1, 3);

        Assert.Equal(3, queue.CurrentIndex);
        Assert.Same(items[1], queue.Current);
    }

    [Fact]
    public void RemoveAt_before_the_current_item_decrements_CurrentIndex()
    {
        var items = Items("a", "b", "c");
        var queue = new PlaybackQueue();
        queue.Load(items, startIndex: 2);

        var wasCurrent = queue.RemoveAt(0);

        Assert.False(wasCurrent);
        Assert.Equal(1, queue.CurrentIndex);
        Assert.Same(items[2], queue.Current);
    }

    [Fact]
    public void RemoveAt_the_current_item_returns_true_and_the_next_entry_takes_its_place()
    {
        var items = Items("a", "b", "c");
        var queue = new PlaybackQueue();
        queue.Load(items, startIndex: 1);

        var wasCurrent = queue.RemoveAt(1);

        Assert.True(wasCurrent);
        Assert.Same(items[2], queue.Current);
        Assert.Equal(1, queue.CurrentIndex);
    }

    [Fact]
    public void Removing_the_last_remaining_item_leaves_no_current_item()
    {
        var queue = new PlaybackQueue();
        queue.Load(Items("a"));

        var wasCurrent = queue.RemoveAt(0);

        Assert.True(wasCurrent);
        Assert.Equal(-1, queue.CurrentIndex);
        Assert.Null(queue.Current);
        Assert.Empty(queue.Items);
    }

    [Fact]
    public void Removing_one_of_two_identical_tracks_removes_only_that_instance()
    {
        var track = TestData.LocalTrack("same");
        var first = PlaybackItem.FromLocal(track);
        var second = PlaybackItem.FromLocal(track);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);

        var queue = new PlaybackQueue();
        queue.Load([first, second, TestPlayback.Item("c")], startIndex: 0);

        var wasCurrent = queue.RemoveAt(1);

        Assert.False(wasCurrent);
        Assert.Equal(2, queue.Items.Count);
        Assert.Same(first, queue.Items[0]);
        Assert.Same(first, queue.Current);
        Assert.Equal(0, queue.CurrentIndex);
    }

    [Fact]
    public void With_shuffle_on_InsertNext_still_plays_the_inserted_item_next()
    {
        var items = Items("a", "b", "c", "d", "e");
        var queue = new PlaybackQueue(new Random(7));
        queue.Load(items, startIndex: 0);
        queue.SetShuffle(true);
        var x = TestPlayback.Item("x");

        queue.InsertNext([x]);
        queue.MoveNext();

        Assert.Same(x, queue.Current);
    }

    [Fact]
    public void Turning_shuffle_off_keeps_the_inserted_item_right_after_the_current_one()
    {
        var items = Items("a", "b", "c", "d");
        var queue = new PlaybackQueue(new Random(7));
        queue.Load(items, startIndex: 0);
        queue.SetShuffle(true);
        var x = TestPlayback.Item("x");
        queue.InsertNext([x]);

        queue.SetShuffle(false);

        Assert.Equal(new[] { items[0], x, items[1], items[2], items[3] }, queue.Items);
        Assert.Same(items[0], queue.Current);
        Assert.Equal(0, queue.CurrentIndex);
    }
}

public class QueueOperationsServiceTests
{
    private readonly FakeAudioPlayer _player = new();
    private readonly PlaybackService _service;

    public QueueOperationsServiceTests() => _service = TestPlayback.Create(_player);

    [Fact]
    public async Task AddToQueueAsync_when_idle_starts_playback()
    {
        var a = TestPlayback.Item("a");

        await _service.AddToQueueAsync([a]);

        Assert.Equal(a, _service.CurrentItem);
        Assert.True(_service.IsPlaying);
        Assert.Equal(a.LocalTrack!.FilePath, _player.LoadedFile);
    }

    [Fact]
    public async Task RemoveFromQueueAsync_of_the_current_entry_plays_the_next_one()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        await _service.PlayAsync([a, b]);

        await _service.RemoveFromQueueAsync(0);

        Assert.Equal(b, _service.CurrentItem);
        Assert.Equal(b.LocalTrack!.FilePath, _player.LoadedFile);
        Assert.True(_service.IsPlaying);
    }

    [Fact]
    public async Task SkipToAsync_changes_the_current_item()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        var c = TestPlayback.Item("c");
        await _service.PlayAsync([a, b, c]);

        await _service.SkipToAsync(2);

        Assert.Equal(c, _service.CurrentItem);
        Assert.Equal(2, _service.CurrentIndex);
        Assert.Equal(c.LocalTrack!.FilePath, _player.LoadedFile);
    }
}

public class QueueActionsTests
{
    private readonly FakeAudioPlayer _player = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly PlaybackService _service;
    private readonly QueueActions _actions;

    public QueueActionsTests()
    {
        _service = TestPlayback.Create(_player);
        _actions = new QueueActions(_service, _dialogs);
    }

    [Fact]
    public async Task AddToQueue_while_playing_shows_an_undo_snackbar_that_removes_the_entry()
    {
        var a = TestPlayback.Item("a");
        var b = TestPlayback.Item("b");
        await _service.PlayAsync([a]);

        await _actions.AddToQueueAsync(b);

        Assert.Equal(2, _service.Queue.Count);
        Assert.Single(_dialogs.Snackbars);

        await _dialogs.Snackbars[0].Action();

        Assert.Single(_service.Queue);
        Assert.Same(a, _service.Queue[0]);
        Assert.Same(a, _service.CurrentItem);
    }

    [Fact]
    public async Task AddToQueue_when_idle_starts_playback_and_shows_no_snackbar()
    {
        var a = TestPlayback.Item("a");

        await _actions.AddToQueueAsync(a);

        Assert.Equal(a, _service.CurrentItem);
        Assert.True(_service.IsPlaying);
        Assert.Empty(_dialogs.Snackbars);
    }
}
