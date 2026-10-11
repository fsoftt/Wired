using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Playback;
using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Presentation.Services;
using Wired.Presentation.ViewModels;
using Wired.Tests.Fakes;

namespace Wired.Tests;

/// <summary>Posts like the MAUI dispatcher: callbacks run later, when the test flushes them.</summary>
internal sealed class DeferredDispatcher : IUiDispatcher
{
    private readonly Queue<Action> _pending = new();

    public void Post(Action action) => _pending.Enqueue(action);

    public void Flush()
    {
        while (_pending.Count > 0)
            _pending.Dequeue()();
    }
}

public sealed class QueueScreenTests : IDisposable
{
    private readonly FakeAudioPlayer _player = new();
    private readonly DeferredDispatcher _dispatcher = new();
    private readonly FakePlugin _plugin = new();
    private readonly TempPaths _paths = new();
    private readonly InMemoryLibrary _library = new();
    private readonly FakeDialogs _dialogs = new();
    private readonly PlaybackService _playback;
    private readonly QueueViewModel _queue;

    public QueueScreenTests()
    {
        _playback = TestPlayback.Create(_player, _plugin);
        _queue = new QueueViewModel(_playback, _dispatcher);
    }

    [Fact]
    public async Task Reorder_works_even_when_refreshes_run_immediately()
    {
        // Regression: each move raised StateChanged and an immediate refresh rebuilt the list mid-reorder.
        var playback = TestPlayback.Create(new FakeAudioPlayer(), _plugin);
        var queue = new QueueViewModel(playback, new ImmediateDispatcher());
        await playback.PlayAsync(Items("now", "a", "b", "c"));
        queue.Activate();

        queue.Upcoming.Move(0, 2); // a b c -> b c a
        queue.ApplyReorder();

        Assert.Equal(["now", "b", "c", "a"], playback.Queue.Select(i => i.Title));
    }

    private static List<PlaybackItem> Items(params string[] titles) => titles.Select(TestPlayback.Item).ToList();

    private static List<string> Titles(IEnumerable<PlaybackItem> items) => items.Select(i => i.Title).ToList();

    private List<string> UpcomingTitles() => _queue.Upcoming.Select(e => e.Title).ToList();

    [Fact]
    public async Task Activate_lists_only_the_entries_after_the_current_one_and_counts_them()
    {
        await _playback.PlayAsync(Items("a", "b", "c", "d"), startIndex: 1);

        _queue.Activate();
        _dispatcher.Flush();

        Assert.True(_queue.HasCurrent);
        Assert.Equal("b", _queue.Current?.Title);
        Assert.Equal(new[] { "c", "d" }, UpcomingTitles());
        Assert.Equal(new[] { 2, 3 }, _queue.Upcoming.Select(e => e.QueueIndex));
        Assert.Equal("2 canciones a continuación", _queue.Summary);
    }

    [Fact]
    public async Task Single_move_then_ApplyReorder_reorders_the_playback_queue_and_keeps_the_current_item()
    {
        var items = Items("a", "b", "c", "d", "e");
        await _playback.PlayAsync(items, startIndex: 0);
        _queue.Activate();

        // Shown order becomes c, d, b, e.
        _queue.Upcoming.Move(0, 2);
        _queue.ApplyReorder();
        _dispatcher.Flush();

        Assert.Equal(new[] { "a", "c", "d", "b", "e" }, Titles(_playback.Queue));
        Assert.Same(items[0], _playback.CurrentItem);
        Assert.Equal(0, _playback.CurrentIndex);
        Assert.Equal(new[] { "c", "d", "b", "e" }, UpcomingTitles());
    }

    [Fact]
    public async Task Two_moves_before_ApplyReorder_end_with_the_queue_in_the_shown_order()
    {
        await _playback.PlayAsync(Items("a", "b", "c", "d", "e", "f"), startIndex: 0);
        _queue.Activate();

        // Shown: b, c, d, e, f. Move f to the top, then e to second place.
        _queue.Upcoming.Move(4, 0);
        _queue.Upcoming.Move(4, 1);
        var shown = UpcomingTitles();
        Assert.Equal(new[] { "f", "e", "b", "c", "d" }, shown);

        _queue.ApplyReorder();
        _dispatcher.Flush();

        Assert.Equal(new[] { "a" }.Concat(shown), Titles(_playback.Queue));
        Assert.Equal(shown, UpcomingTitles());
        Assert.Equal("a", _playback.CurrentItem?.Title);
    }

    [Fact]
    public async Task RemoveCommand_on_an_entry_removes_it_from_the_queue_and_refreshes_upcoming()
    {
        await _playback.PlayAsync(Items("a", "b", "c"), startIndex: 0);
        _queue.Activate();

        await _queue.Upcoming[0].RemoveCommand.ExecuteAsync(null);
        _dispatcher.Flush();

        Assert.Equal(new[] { "a", "c" }, Titles(_playback.Queue));
        Assert.Equal(new[] { "c" }, UpcomingTitles());
        Assert.Equal("1 canción a continuación", _queue.Summary);
    }

    [Fact]
    public async Task PlayCommand_on_an_entry_makes_it_the_current_item()
    {
        var items = Items("a", "b", "c");
        await _playback.PlayAsync(items, startIndex: 0);
        _queue.Activate();

        await _queue.Upcoming[1].PlayCommand.ExecuteAsync(null);
        _dispatcher.Flush();

        Assert.Same(items[2], _playback.CurrentItem);
        Assert.Equal(2, _playback.CurrentIndex);
        Assert.Equal("c", _queue.Current?.Title);
        Assert.Empty(UpcomingTitles());
    }

    [Fact]
    public async Task ClearCommand_leaves_only_the_current_item_in_the_queue()
    {
        var items = Items("a", "b", "c", "d");
        await _playback.PlayAsync(items, startIndex: 0);
        _queue.Activate();

        await _queue.ClearCommand.ExecuteAsync(null);
        _dispatcher.Flush();

        Assert.Single(_playback.Queue);
        Assert.Same(items[0], _playback.CurrentItem);
        Assert.Empty(_queue.Upcoming);
        Assert.False(_queue.HasUpcoming);
        Assert.Equal("No hay nada más en la cola", _queue.Summary);
    }

    [Fact]
    public async Task Play_on_a_completed_download_plays_the_completed_downloads_starting_at_it()
    {
        var downloads = new DownloadQueue(
            new DownloadTrackUseCase(new PluginCatalog([_plugin]), _library, _paths, new FixedClock(DateTimeOffset.Now)));
        var vm = CreateDownloads(downloads);

        var first = downloads.Enqueue(TestData.RemoteTrack("a"));
        var second = downloads.Enqueue(TestData.RemoteTrack("b"));
        await TestWait.UntilAsync(() => first.Status == DownloadStatus.Completed && second.Status == DownloadStatus.Completed);

        // Newest first in the list, so "a" is the second row.
        var target = vm.Jobs.Single(j => j.Job == first);
        await target.PlayCommand.ExecuteAsync(null);

        Assert.Equal(2, _playback.Queue.Count);
        Assert.Equal(vm.Jobs.Select(j => j.Title), Titles(_playback.Queue));
        Assert.Equal(vm.Jobs.IndexOf(target), _playback.CurrentIndex);
        Assert.Equal("Title a", _playback.CurrentItem?.Title);
        Assert.False(_playback.CurrentItem!.IsStream);
    }

    [Fact]
    public async Task Play_on_a_download_still_in_progress_streams_just_that_track()
    {
        _plugin.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloads = new DownloadQueue(
            new DownloadTrackUseCase(new PluginCatalog([_plugin]), _library, _paths, new FixedClock(DateTimeOffset.Now)));
        var vm = CreateDownloads(downloads);

        var job = downloads.Enqueue(TestData.RemoteTrack("a"));
        await TestWait.UntilAsync(() => job.Status == DownloadStatus.Downloading);

        await vm.Jobs.Single().PlayCommand.ExecuteAsync(null);

        Assert.Single(_playback.Queue);
        Assert.True(_playback.CurrentItem!.IsStream);
        Assert.Equal("stream:a", _player.LoadedStream);

        _plugin.Gate.SetResult();
        await TestWait.UntilAsync(() => job.Status == DownloadStatus.Completed);
    }

    [Fact]
    public async Task AddToQueueCommand_on_a_download_appends_it_to_the_playback_queue()
    {
        var downloads = new DownloadQueue(
            new DownloadTrackUseCase(new PluginCatalog([_plugin]), _library, _paths, new FixedClock(DateTimeOffset.Now)));
        var vm = CreateDownloads(downloads);

        var job = downloads.Enqueue(TestData.RemoteTrack("a"));
        await TestWait.UntilAsync(() => job.Status == DownloadStatus.Completed);

        var current = TestPlayback.Item("current");
        await _playback.PlayAsync([current]);

        await vm.Jobs.Single().AddToQueueCommand.ExecuteAsync(null);

        Assert.Equal(2, _playback.Queue.Count);
        Assert.Same(current, _playback.CurrentItem);
        Assert.Equal("Title a", _playback.Queue[1].Title);
        Assert.Single(_dialogs.Snackbars);
    }

    private DownloadsViewModel CreateDownloads(DownloadQueue downloads)
    {
        var clock = new FixedClock(DateTimeOffset.Now);
        var actions = new TrackCollectionActions(
            new LikesService(new JsonLikedTracksRepository(_paths), _library),
            new PlaylistService(new JsonPlaylistRepository(_paths), _library, clock),
            new QueueActions(_playback, _dialogs),
            _dialogs);
        return new DownloadsViewModel(downloads, _dialogs, new ImmediateDispatcher(), actions, _playback);
    }

    public void Dispose() => _paths.Dispose();
}
