using Wired.Core.Playback;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public class PlaybackQueueTests
{
    private static List<PlaybackItem> Items(params string[] titles) => titles.Select(TestPlayback.Item).ToList();

    [Fact]
    public void Navigates_forward_and_backward_within_bounds()
    {
        var queue = new PlaybackQueue();
        queue.Load(Items("a", "b"));

        Assert.Equal("a", queue.Current!.Title);
        Assert.False(queue.MovePrevious());
        Assert.True(queue.MoveNext());
        Assert.Equal("b", queue.Current!.Title);
        Assert.False(queue.MoveNext());
    }

    [Fact]
    public void Repeat_all_wraps_around_both_ways()
    {
        var queue = new PlaybackQueue { Repeat = RepeatMode.All };
        queue.Load(Items("a", "b"));

        Assert.True(queue.MovePrevious());
        Assert.Equal("b", queue.Current!.Title);
        Assert.True(queue.MoveNext());
        Assert.Equal("a", queue.Current!.Title);
    }

    [Fact]
    public void Removing_an_item_before_the_cursor_keeps_the_current_item()
    {
        var items = Items("a", "b", "c");
        var queue = new PlaybackQueue();
        queue.Load(items, startIndex: 1);

        queue.Remove(items[0].Id);

        Assert.Equal("b", queue.Current!.Title);
    }

    [Fact]
    public void Shuffle_keeps_current_first_and_turning_it_off_restores_order()
    {
        var items = Items(Enumerable.Range(0, 20).Select(i => $"t{i}").ToArray());
        var queue = new PlaybackQueue(new Random(42));
        queue.Load(items, startIndex: 5);

        queue.SetShuffle(true);
        Assert.Equal("t5", queue.Current!.Title);
        Assert.Equal(0, queue.CurrentIndex);
        Assert.Equal(items.Select(i => i.Id).Order(), queue.Items.Select(i => i.Id).Order());
        Assert.NotEqual(items.Select(i => i.Id), queue.Items.Select(i => i.Id));

        queue.MoveNext();
        var playing = queue.Current!;
        queue.SetShuffle(false);

        Assert.Equal(items.Select(i => i.Id), queue.Items.Select(i => i.Id));
        Assert.Same(playing, queue.Current);
    }

    [Fact]
    public void Loading_with_shuffle_on_starts_with_the_chosen_item()
    {
        var items = Items("a", "b", "c", "d");
        var queue = new PlaybackQueue(new Random(1));
        queue.SetShuffle(true);

        queue.Load(items, startIndex: 2);

        Assert.Equal("c", queue.Current!.Title);
        Assert.True(queue.IsShuffled);
    }
}
