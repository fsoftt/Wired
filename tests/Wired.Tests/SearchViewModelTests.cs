using CommunityToolkit.Mvvm.Messaging;
using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Infrastructure.Collections;
using Wired.Presentation.Services;
using Wired.Core.Playback;
using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Presentation.ViewModels;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class SearchViewModelTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly FakePlugin _demo = new("demo");
    private readonly FakePlugin _other = new("other");
    private readonly InMemoryLibrary _library = new();
    private readonly SearchViewModel _viewModel;

    public SearchViewModelTests()
    {
        var catalog = new PluginCatalog([_demo, _other]);
        var playback = TestPlayback.Create(new FakeAudioPlayer(), _demo, _other);
        var dialogs = new FakeDialogs();
        var clock = new FixedClock(DateTimeOffset.Now);
        var actions = new TrackCollectionActions(
            new LikesService(new JsonLikedTracksRepository(_paths), _library),
            new PlaylistService(new JsonPlaylistRepository(_paths), _library, clock),
            new QueueActions(playback, dialogs),
            dialogs);
        _viewModel = new SearchViewModel(
            catalog,
            new SearchTracksUseCase(catalog),
            new DownloadQueue(new DownloadTrackUseCase(catalog, _library, _paths, clock)),
            _library,
            playback,
            new ImmediateDispatcher(),
            new WeakReferenceMessenger(),
            actions);
    }

    [Fact]
    public void First_plugin_is_selected_by_default()
    {
        Assert.Equal("demo", _viewModel.SelectedPlugin?.Id);
        Assert.True(_viewModel.Plugins[0].IsSelected);
        Assert.False(_viewModel.Plugins[1].IsSelected);
    }

    [Fact]
    public async Task Searches_with_the_selected_plugin()
    {
        _other.SearchResults.Add(TestData.RemoteTrack("from-other"));
        _viewModel.SelectPluginCommand.Execute(_viewModel.Plugins[1]);
        _viewModel.Query = "anything";

        await _viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(_viewModel.HasResults);
        Assert.Equal("from-other", Assert.Single(_viewModel.Results).Track.RemoteId);
    }

    [Fact]
    public async Task Shows_empty_state_when_nothing_is_found()
    {
        _viewModel.Query = "nothing";

        await _viewModel.SearchCommand.ExecuteAsync(null);

        Assert.True(_viewModel.HasNoResults);
    }

    [Fact]
    public async Task Downloading_a_result_marks_it_as_downloaded()
    {
        _demo.SearchResults.Add(new("demo", "v1", "Song", "Artist", null, null));
        _viewModel.Query = "song";
        await _viewModel.SearchCommand.ExecuteAsync(null);
        var result = _viewModel.Results[0];

        result.DownloadCommand.Execute(null);

        await TestWait.UntilAsync(() => result.IsDownloaded);
        Assert.Single(_library.Tracks);
    }

    [Fact]
    public async Task Tapping_a_result_streams_it_without_downloading()
    {
        _demo.SearchResults.Add(new("demo", "v1", "Song", "Artist", null, null));
        _viewModel.Query = "song";
        await _viewModel.SearchCommand.ExecuteAsync(null);

        await _viewModel.Results[0].PlayCommand.ExecuteAsync(null);

        Assert.True(_viewModel.Results[0].IsCurrent);
        Assert.Empty(_library.Tracks);
        Assert.Equal(0, _demo.DownloadCount);
    }

    public void Dispose() => _paths.Dispose();
}
