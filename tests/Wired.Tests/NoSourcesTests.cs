using CommunityToolkit.Mvvm.Messaging;
using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Presentation.Services;
using Wired.Presentation.ViewModels;
using Wired.Tests.Fakes;

namespace Wired.Tests;

/// <summary>The app is usable with no music source installed: nothing assumes a plugin exists.</summary>
public sealed class NoSourcesTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly InMemoryLibrary _library = new();
    private readonly PluginCatalog _catalog = new([]);
    private readonly FakeDialogs _dialogs = new();

    public void Dispose() => _paths.Dispose();

    [Fact]
    public void Search_has_no_sources_selected_and_cannot_search()
    {
        var clock = new FixedClock(DateTimeOffset.Now);
        var playback = TestPlayback.Create(new FakeAudioPlayer());
        var actions = new TrackCollectionActions(
            new LikesService(new JsonLikedTracksRepository(_paths), _library),
            new PlaylistService(new JsonPlaylistRepository(_paths), _library, clock),
            new QueueActions(playback, _dialogs),
            _dialogs);
        var viewModel = new SearchViewModel(
            _catalog,
            new SearchTracksUseCase(_catalog),
            new DownloadQueue(new DownloadTrackUseCase(_catalog, _library, _paths, clock)),
            _library,
            playback,
            new ImmediateDispatcher(),
            new WeakReferenceMessenger(),
            actions);

        viewModel.Query = "anything";

        Assert.Empty(viewModel.Plugins);
        Assert.Null(viewModel.SelectedPlugin);
        Assert.True(viewModel.HasNoSources);
        Assert.False(viewModel.HasSources);
        Assert.False(viewModel.ShowIdle);
        Assert.False(viewModel.SearchCommand.CanExecute(null));
        Assert.Equal("No hay fuentes de música instaladas", viewModel.SearchPlaceholder);
    }

    [Fact]
    public async Task Shared_link_shows_a_generic_toast()
    {
        var queue = new DownloadQueue(new DownloadTrackUseCase(_catalog, _library, _paths, new FixedClock(DateTimeOffset.Now)));
        var handler = new SharedContentHandler(new DownloadSharedLinkUseCase(_catalog, queue), _dialogs, new NoNavigation());

        await handler.HandleTextAsync("Mira https://example.com/song");

        Assert.Equal(["Wired no reconoce ese enlace con las fuentes instaladas."], _dialogs.Toasts);
        Assert.Empty(_dialogs.Errors);
        Assert.Empty(queue.Jobs);
    }

    [Fact]
    public async Task Home_sections_are_empty_instead_of_failing()
    {
        var feed = new GetHomeFeedUseCase(_catalog, _library, new JsonLikedTracksRepository(_paths), new InMemoryHomeFeedCache(), new FixedClock(DateTimeOffset.Now));
        var playback = TestPlayback.Create(new FakeAudioPlayer());
        var clock = new FixedClock(DateTimeOffset.Now);
        var actions = new TrackCollectionActions(
            new LikesService(new JsonLikedTracksRepository(_paths), _library),
            new PlaylistService(new JsonPlaylistRepository(_paths), _library, clock),
            new QueueActions(playback, _dialogs),
            _dialogs);
        var home = new HomeViewModel(
            feed,
            new DownloadQueue(new DownloadTrackUseCase(_catalog, _library, _paths, clock)),
            _library,
            playback,
            new ImmediateDispatcher(),
            new WeakReferenceMessenger(),
            actions);

        await home.OnAppearingAsync();

        Assert.True(home.HasNoTrending);
        Assert.True(home.HasNoTrendingColombia);
        Assert.True(home.HasNoForYou);
        Assert.False(home.HasTrendingError);
        Assert.Equal("Top global", home.TrendingSubtitle);
        Assert.Equal("Ninguna fuente instalada ofrece tendencias.", home.TrendingEmptyText);
    }

    [Fact]
    public void Home_subtitle_names_the_discoverable_source()
    {
        var plugin = new FakePlugin("demo");
        var catalog = new PluginCatalog([plugin]);
        var feed = new GetHomeFeedUseCase(catalog, _library, new JsonLikedTracksRepository(_paths), new InMemoryHomeFeedCache(), new FixedClock(DateTimeOffset.Now));

        Assert.Equal([plugin.Info.DisplayName], feed.DiscoverableSourceNames);
    }

    private sealed class NoNavigation : INavigationService
    {
        public Task OpenNowPlayingAsync() => Task.CompletedTask;

        public Task OpenPlaylistAsync(Guid? playlistId) => Task.CompletedTask;

        public Task OpenQueueAsync() => Task.CompletedTask;

        public Task OpenDownloadsAsync() => Task.CompletedTask;

        public Task OpenImportProblemsAsync(Guid sessionId) => Task.CompletedTask;

        public Task GoBackAsync() => Task.CompletedTask;
    }
}
