using CommunityToolkit.Mvvm.Messaging;
using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.Resilience;
using Wired.Core.Throttling;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Presentation.Services;
using Wired.Presentation.ViewModels;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class HomeFeedTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TempPaths _paths = new();
    private readonly FakePlugin _plugin = new();
    private readonly InMemoryLibrary _library = new();
    private readonly InMemoryHomeFeedCache _cache = new();
    private readonly ManualClock _clock = new(Start);
    private readonly JsonLikedTracksRepository _likes;
    private readonly PluginCatalog _catalog;
    private readonly GetHomeFeedUseCase _feed;

    public HomeFeedTests()
    {
        _likes = new JsonLikedTracksRepository(_paths);
        _catalog = new PluginCatalog([_plugin]);
        _feed = new GetHomeFeedUseCase(_catalog, _library, _likes, _cache, _clock);
    }

    public void Dispose() => _paths.Dispose();

    private static RemoteTrack Remote(string id) => TestData.RemoteTrack(id);

    private LocalTrack AddToLibrary(string id, int minutesAgo = 0)
    {
        var track = TestData.LocalTrack(id) with { AddedAt = Start.AddMinutes(-minutesAgo) };
        _library.Tracks.Add(track);
        return track;
    }

    [Fact]
    public async Task Trending_comes_from_the_discoverable_sources()
    {
        _plugin.TrendingResults.AddRange([Remote("a"), Remote("b")]);

        var section = await _feed.LoadAsync(HomeSectionKind.Trending);

        Assert.Equal(["a", "b"], section.Tracks.Select(t => t.RemoteId));
    }

    [Fact]
    public async Task For_you_is_empty_without_a_library_and_makes_no_request()
    {
        var section = await _feed.LoadAsync(HomeSectionKind.ForYou);

        Assert.Empty(section.Tracks);
        Assert.Empty(_plugin.RelatedCalls);
    }

    [Fact]
    public async Task For_you_seeds_liked_tracks_first_then_the_newest_ones()
    {
        var oldLiked = AddToLibrary("old-liked", minutesAgo: 500);
        AddToLibrary("older", minutesAgo: 400);
        AddToLibrary("newest", minutesAgo: 1);
        AddToLibrary("newer", minutesAgo: 10);
        AddToLibrary("middle", minutesAgo: 100);
        await _likes.SetLikedAsync(oldLiked.Id, true);

        await _feed.LoadAsync(HomeSectionKind.ForYou);

        Assert.Equal(
            ["newer", "newest", "old-liked"],
            _plugin.RelatedCalls.Select(c => c.RemoteId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task For_you_only_seeds_from_discoverable_sources()
    {
        _library.Tracks.Add(TestData.LocalTrack("elsewhere") with { SourceId = "other" });

        await _feed.LoadAsync(HomeSectionKind.ForYou);

        Assert.Empty(_plugin.RelatedCalls);
    }

    [Fact]
    public async Task For_you_interleaves_and_removes_duplicates_seeds_and_owned_tracks()
    {
        AddToLibrary("s1", minutesAgo: 1);
        AddToLibrary("s2", minutesAgo: 2);
        AddToLibrary("owned", minutesAgo: 3);
        _plugin.RelatedResults["s1"] = [Remote("s2"), Remote("x1"), Remote("shared"), Remote("x2")];
        _plugin.RelatedResults["s2"] = [Remote("owned"), Remote("y1"), Remote("shared"), Remote("s1")];
        _plugin.RelatedResults["owned"] = [Remote("z1")];

        var section = await _feed.LoadAsync(HomeSectionKind.ForYou);

        // One track from each seed in turn; the seeds and the owned song never show up.
        Assert.Equal(["z1", "x1", "y1", "shared", "x2"], section.Tracks.Select(t => t.RemoteId));
    }

    [Fact]
    public async Task For_you_is_capped()
    {
        AddToLibrary("seed");
        _plugin.RelatedResults["seed"] = Enumerable.Range(0, 60).Select(i => Remote($"r{i}")).ToList();

        var section = await _feed.LoadAsync(HomeSectionKind.ForYou);

        Assert.Equal(GetHomeFeedUseCase.ForYouMaxResults, section.Tracks.Count);
    }

    [Fact]
    public async Task For_you_requests_run_with_background_priority()
    {
        AddToLibrary("seed");

        await _feed.LoadAsync(HomeSectionKind.ForYou);

        Assert.All(_plugin.RelatedCalls, call => Assert.True(call.Background));
        Assert.False(RequestPriority.IsBackground);
    }

    [Fact]
    public async Task A_fresh_cached_section_makes_no_request()
    {
        _plugin.TrendingResults.Add(Remote("a"));
        await _feed.LoadAsync(HomeSectionKind.Trending);

        _clock.Now = Start + GetHomeFeedUseCase.TrendingTimeToLive - TimeSpan.FromMinutes(1);
        var again = await _feed.LoadAsync(HomeSectionKind.Trending);

        Assert.Equal(1, _plugin.TrendingCalls);
        Assert.Equal("a", Assert.Single(again.Tracks).RemoteId);
        Assert.Equal(Start, again.FetchedAt);
    }

    [Fact]
    public async Task A_section_older_than_its_ttl_is_fetched_again()
    {
        _plugin.TrendingResults.Add(Remote("a"));
        await _feed.LoadAsync(HomeSectionKind.Trending);

        _plugin.TrendingResults[0] = Remote("b");
        _clock.Now = Start + GetHomeFeedUseCase.TrendingTimeToLive;
        var again = await _feed.LoadAsync(HomeSectionKind.Trending);

        Assert.Equal(2, _plugin.TrendingCalls);
        Assert.Equal("b", Assert.Single(again.Tracks).RemoteId);
        Assert.Equal(_clock.Now, _cache.Entries[HomeSectionKind.Trending].FetchedAt);
    }

    [Fact]
    public async Task For_you_lives_longer_than_trending()
    {
        AddToLibrary("seed");
        _plugin.RelatedResults["seed"] = [Remote("r")];
        await _feed.LoadAsync(HomeSectionKind.ForYou);

        _clock.Now = Start + GetHomeFeedUseCase.TrendingTimeToLive + TimeSpan.FromMinutes(1);
        await _feed.LoadAsync(HomeSectionKind.ForYou);

        Assert.Single(_plugin.RelatedCalls);
    }

    [Fact]
    public async Task Forcing_refetches_a_fresh_section()
    {
        _plugin.TrendingResults.Add(Remote("a"));
        await _feed.LoadAsync(HomeSectionKind.Trending);

        await _feed.LoadAsync(HomeSectionKind.Trending, force: true);

        Assert.Equal(2, _plugin.TrendingCalls);
    }

    [Fact]
    public async Task Rate_limit_keeps_the_cached_content()
    {
        _plugin.TrendingResults.Add(Remote("a"));
        await _feed.LoadAsync(HomeSectionKind.Trending);

        _plugin.DiscoveryError = new SourceRateLimitedException("slow down");
        var section = await _feed.LoadAsync(HomeSectionKind.Trending, force: true);

        Assert.Equal(HomeFeedIssue.RateLimited, section.Issue);
        Assert.Equal("a", Assert.Single(section.Tracks).RemoteId);
        Assert.Equal(Start, _cache.Entries[HomeSectionKind.Trending].FetchedAt);
    }

    [Fact]
    public async Task Other_failures_also_keep_the_cached_content()
    {
        _plugin.TrendingResults.Add(Remote("a"));
        await _feed.LoadAsync(HomeSectionKind.Trending);

        _plugin.DiscoveryError = new HttpRequestException("offline");
        var section = await _feed.LoadAsync(HomeSectionKind.Trending, force: true);

        Assert.Equal(HomeFeedIssue.Failed, section.Issue);
        Assert.Single(section.Tracks);
    }

    [Fact]
    public async Task Failure_without_a_cache_is_an_error()
    {
        _plugin.DiscoveryError = new SourceRateLimitedException("slow down");

        await Assert.ThrowsAsync<SourceRateLimitedException>(() => _feed.LoadAsync(HomeSectionKind.Trending));
        Assert.Empty(_cache.Entries);
    }

    [Fact]
    public async Task Cached_recommendations_leave_out_what_was_downloaded_since()
    {
        AddToLibrary("seed");
        _plugin.RelatedResults["seed"] = [Remote("a"), Remote("b")];
        await _feed.LoadAsync(HomeSectionKind.ForYou);

        AddToLibrary("a");
        var section = await _feed.LoadAsync(HomeSectionKind.ForYou);

        Assert.Equal("b", Assert.Single(section.Tracks).RemoteId);
    }

    [Fact]
    public async Task One_section_failing_does_not_break_the_other()
    {
        AddToLibrary("seed");
        _plugin.TrendingResults.Add(Remote("t"));

        var trending = await _feed.LoadAsync(HomeSectionKind.Trending);
        _plugin.DiscoveryError = new HttpRequestException("offline");

        await Assert.ThrowsAsync<HttpRequestException>(() => _feed.LoadAsync(HomeSectionKind.ForYou));
        Assert.Equal("t", Assert.Single(trending.Tracks).RemoteId);
        Assert.Equal("t", Assert.Single((await _feed.GetCachedAsync(HomeSectionKind.Trending))!.Tracks).RemoteId);
    }

    [Fact]
    public void Wrapping_keeps_the_discovery_capability()
    {
        var throttle = new RequestThrottle("fake", new ThrottleOptions());
        var network = new AlwaysOnlineNetworkMonitor();

        Assert.IsAssignableFrom<IDiscoverableMusicSource>(ThrottledMusicSource.Wrap(_plugin, throttle));
        Assert.IsAssignableFrom<IStreamableMusicSource>(ThrottledMusicSource.Wrap(_plugin, throttle));
        Assert.IsAssignableFrom<IDiscoverableMusicSource>(NetworkResilientMusicSource.Wrap(ThrottledMusicSource.Wrap(_plugin, throttle), network, new ResilienceOptions()));
        Assert.IsAssignableFrom<IStreamableMusicSource>(NetworkResilientMusicSource.Wrap(ThrottledMusicSource.Wrap(_plugin, throttle), network, new ResilienceOptions()));
        Assert.IsNotAssignableFrom<IDiscoverableMusicSource>(ThrottledMusicSource.Wrap(new StreamOnlyPlugin(), throttle));
        Assert.IsNotAssignableFrom<IDiscoverableMusicSource>(NetworkResilientMusicSource.Wrap(new StreamOnlyPlugin(), network, new ResilienceOptions()));
    }

    [Fact]
    public async Task Decorated_source_forwards_discovery_calls_through_the_throttle()
    {
        var throttle = new RequestThrottle("fake", new ThrottleOptions());
        var source = (IDiscoverableMusicSource)ThrottledMusicSource.Wrap(_plugin, throttle);
        _plugin.TrendingResults.Add(Remote("a"));
        _plugin.RelatedResults["seed"] = [Remote("b")];

        Assert.Equal("a", Assert.Single(await source.GetTrendingAsync(null, 5)).RemoteId);
        using (RequestPriority.Background())
            Assert.Equal("b", Assert.Single(await source.GetRelatedAsync("seed", 5)).RemoteId);

        Assert.Equal(("seed", true), Assert.Single(_plugin.RelatedCalls));
    }

    [Fact]
    public async Task Rate_limit_of_a_decorated_source_reaches_the_feed_and_cools_the_throttle()
    {
        var throttle = new RequestThrottle("fake", new ThrottleOptions());
        var catalog = new PluginCatalog([ThrottledMusicSource.Wrap(_plugin, throttle)]);
        var feed = new GetHomeFeedUseCase(catalog, _library, _likes, _cache, _clock);
        _plugin.DiscoveryError = new SourceRateLimitedException("slow down");

        await Assert.ThrowsAsync<SourceRateLimitedException>(() => feed.LoadAsync(HomeSectionKind.Trending));
        Assert.True(throttle.IsCoolingDown);
    }

    [Fact]
    public async Task A_timeout_is_a_failure_that_falls_back_to_the_cache()
    {
        _plugin.TrendingResults.Add(Remote("a"));
        await _feed.LoadAsync(HomeSectionKind.Trending);

        // An HTTP timeout surfaces as a cancellation, but nobody canceled the load.
        _plugin.DiscoveryError = new TaskCanceledException("HttpClient timeout");
        var section = await _feed.LoadAsync(HomeSectionKind.Trending, force: true);

        Assert.Equal(HomeFeedIssue.Failed, section.Issue);
        Assert.Single(section.Tracks);
    }

    [Fact]
    public async Task A_timeout_without_a_cache_is_an_error_not_a_cancellation_of_the_load()
    {
        _plugin.DiscoveryError = new TaskCanceledException("HttpClient timeout");

        await Assert.ThrowsAsync<TaskCanceledException>(() => _feed.LoadAsync(HomeSectionKind.Trending));
    }

    [Fact]
    public async Task Canceling_the_load_still_cancels()
    {
        _plugin.TrendingResults.Add(Remote("a"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _feed.LoadAsync(HomeSectionKind.Trending, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task Failing_to_save_the_cache_keeps_the_fresh_tracks()
    {
        _plugin.TrendingResults.Add(Remote("a"));
        var feed = new GetHomeFeedUseCase(_catalog, _library, _likes, new BrokenCache(), _clock);

        var section = await feed.LoadAsync(HomeSectionKind.Trending);

        Assert.Equal("a", Assert.Single(section.Tracks).RemoteId);
        Assert.Equal(HomeFeedIssue.None, section.Issue);
        Assert.Null(await feed.GetCachedAsync(HomeSectionKind.Trending));
    }

    [Fact]
    public async Task A_copy_from_the_future_is_stale()
    {
        _plugin.TrendingResults.Add(Remote("a"));
        await _feed.LoadAsync(HomeSectionKind.Trending);

        _clock.Now = Start - TimeSpan.FromHours(1);
        await _feed.LoadAsync(HomeSectionKind.Trending);

        Assert.Equal(2, _plugin.TrendingCalls);
    }

    [Fact]
    public async Task File_cache_round_trips_and_leaves_no_temp_files()
    {
        var cache = new Wired.Infrastructure.Home.FileHomeFeedCache(_paths);
        var entry = new HomeFeedEntry(Start, [Remote("a")]);

        await cache.SetAsync(HomeSectionKind.Trending, entry);
        await cache.SetAsync(HomeSectionKind.Trending, entry);

        var loaded = await cache.GetAsync(HomeSectionKind.Trending);
        Assert.Equal(Start, loaded!.FetchedAt);
        Assert.Equal("a", Assert.Single(loaded.Tracks).RemoteId);
        Assert.Null(await cache.GetAsync(HomeSectionKind.ForYou));
        Assert.Single(Directory.GetFiles(Path.Combine(_paths.DataDirectory, "home")));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    public async Task File_cache_treats_damaged_content_as_missing(string content)
    {
        var directory = Path.Combine(_paths.DataDirectory, "home");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "trending.json"), content);

        Assert.Null(await new Wired.Infrastructure.Home.FileHomeFeedCache(_paths).GetAsync(HomeSectionKind.Trending));
    }

    [Fact]
    public async Task A_cache_that_cannot_be_read_does_not_fail_the_section()
    {
        _plugin.TrendingResults.Add(Remote("a"));
        var feed = new GetHomeFeedUseCase(_catalog, _library, _likes, new BrokenCache(), _clock);

        Assert.Single((await feed.LoadAsync(HomeSectionKind.Trending)).Tracks);
        Assert.Null(await feed.GetCachedAsync(HomeSectionKind.ForYou));
    }

    private sealed class BrokenCache : IHomeFeedCache
    {
        public Task<HomeFeedEntry?> GetAsync(HomeSectionKind kind, CancellationToken cancellationToken = default) =>
            throw new IOException("disk");

        public Task SetAsync(HomeSectionKind kind, HomeFeedEntry entry, CancellationToken cancellationToken = default) =>
            throw new IOException("disk");
    }

    // --- View model ---

    private TrackCollectionActions CreateActions()
    {
        var playback = TestPlayback.Create(new FakeAudioPlayer(), _plugin);
        var dialogs = new FakeDialogs();
        return new TrackCollectionActions(
            new LikesService(_likes, _library),
            new PlaylistService(new JsonPlaylistRepository(_paths), _library, _clock),
            new QueueActions(playback, dialogs),
            dialogs);
    }

    private HomeViewModel CreateViewModel(IHomeFeedCache? cache = null) => new(
        new GetHomeFeedUseCase(_catalog, _library, _likes, cache ?? _cache, _clock),
        new DownloadQueue(new DownloadTrackUseCase(_catalog, _library, _paths, _clock)),
        _library,
        TestPlayback.Create(new FakeAudioPlayer(), _plugin),
        new ImmediateDispatcher(),
        new WeakReferenceMessenger(),
        CreateActions());

    [Fact]
    public async Task View_model_shows_both_sections()
    {
        AddToLibrary("seed");
        _plugin.TrendingResults.Add(Remote("t"));
        _plugin.RelatedResults["seed"] = [Remote("r")];
        var viewModel = CreateViewModel();

        await viewModel.OnAppearingAsync();

        Assert.True(viewModel.HasTrending);
        Assert.True(viewModel.HasForYou);
        Assert.Equal("t", Assert.Single(viewModel.Trending).Track.RemoteId);
        Assert.Equal("r", Assert.Single(viewModel.ForYou).Track.RemoteId);
        Assert.False(viewModel.HasNotice);
    }

    [Fact]
    public async Task View_model_with_an_empty_library_shows_the_for_you_empty_state()
    {
        _plugin.TrendingResults.Add(Remote("t"));
        var viewModel = CreateViewModel();

        await viewModel.OnAppearingAsync();

        Assert.True(viewModel.HasTrending);
        Assert.True(viewModel.HasNoForYou);
    }

    [Fact]
    public async Task View_model_errors_are_per_section_and_refresh_recovers()
    {
        AddToLibrary("seed");
        var viewModel = CreateViewModel();
        _plugin.DiscoveryError = new HttpRequestException("offline");

        await viewModel.OnAppearingAsync();

        Assert.True(viewModel.HasTrendingError);
        Assert.True(viewModel.HasForYouError);

        _plugin.DiscoveryError = null;
        _plugin.TrendingResults.Add(Remote("t"));
        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasTrending);
        Assert.True(viewModel.HasNoForYou);
    }

    [Fact]
    public async Task View_model_loads_once_and_not_on_every_appearance()
    {
        _plugin.TrendingResults.Add(Remote("t"));
        var viewModel = CreateViewModel();

        await viewModel.OnAppearingAsync();
        viewModel.OnDisappearing();
        await viewModel.OnAppearingAsync();

        Assert.Equal(1, _plugin.TrendingCalls);
    }

    [Fact]
    public async Task View_model_shows_the_cache_and_a_notice_when_rate_limited()
    {
        _plugin.TrendingResults.Add(Remote("cached"));
        await _feed.LoadAsync(HomeSectionKind.Trending);
        _clock.Now = Start + TimeSpan.FromDays(1);
        _plugin.DiscoveryError = new SourceRateLimitedException("slow down");
        var viewModel = CreateViewModel();

        await viewModel.OnAppearingAsync();

        Assert.True(viewModel.HasTrending);
        Assert.Equal("cached", Assert.Single(viewModel.Trending).Track.RemoteId);
        Assert.True(viewModel.HasNotice);
    }

    [Fact]
    public async Task View_model_refresh_forces_a_fetch_but_is_ignored_while_loading()
    {
        _plugin.TrendingResults.Add(Remote("t"));
        var viewModel = CreateViewModel();
        await viewModel.OnAppearingAsync();

        await viewModel.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(2, _plugin.TrendingCalls);
        Assert.False(viewModel.IsRefreshing);

        // A load that is still running swallows a second refresh.
        var gate = new TaskCompletionSource();
        var slowViewModel = CreateViewModel(new GatedCache(_cache, gate.Task));
        var first = slowViewModel.OnAppearingAsync();
        await slowViewModel.RefreshCommand.ExecuteAsync(null);
        gate.SetResult();
        await first;

        Assert.Equal(2, _plugin.TrendingCalls); // The cached copy was still fresh and nothing was forced.
    }

    [Fact]
    public async Task View_model_timeout_ends_in_an_error_state_not_a_stuck_spinner()
    {
        _plugin.DiscoveryError = new TaskCanceledException("HttpClient timeout");
        var viewModel = CreateViewModel();

        await viewModel.OnAppearingAsync();

        Assert.True(viewModel.HasTrendingError);
        Assert.False(viewModel.IsTrendingLoading);
    }

    [Fact]
    public async Task View_model_retry_reloads_only_its_section()
    {
        AddToLibrary("seed");
        _plugin.DiscoveryError = new HttpRequestException("offline");
        var viewModel = CreateViewModel();
        await viewModel.OnAppearingAsync();
        _plugin.DiscoveryError = null;
        _plugin.TrendingResults.Add(Remote("t"));
        var trendingCalls = _plugin.TrendingCalls;
        var relatedCalls = _plugin.RelatedCalls.Count;

        await viewModel.RetryTrendingCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasTrending);
        Assert.True(viewModel.HasForYouError);
        Assert.Equal(trendingCalls + 1, _plugin.TrendingCalls);
        Assert.Equal(relatedCalls, _plugin.RelatedCalls.Count);
    }

    [Fact]
    public async Task View_model_tries_again_on_the_next_appearance_after_an_error()
    {
        _plugin.DiscoveryError = new HttpRequestException("offline");
        var viewModel = CreateViewModel();
        await viewModel.OnAppearingAsync();
        Assert.True(viewModel.HasTrendingError);

        _plugin.DiscoveryError = null;
        _plugin.TrendingResults.Add(Remote("t"));
        await viewModel.OnAppearingAsync();

        Assert.True(viewModel.HasTrending);
    }

    [Fact]
    public async Task Trending_colombia_asks_the_sources_for_the_colombian_chart()
    {
        _plugin.TrendingResults.Add(Remote("global"));
        _plugin.RegionalTrendingResults["CO"] = [Remote("co1"), Remote("co2")];

        var section = await _feed.LoadAsync(HomeSectionKind.TrendingColombia);

        Assert.Equal(["co1", "co2"], section.Tracks.Select(t => t.RemoteId));
        Assert.Equal(["CO"], _plugin.TrendingRegionCalls);
    }

    [Fact]
    public async Task Trending_colombia_has_its_own_cache_and_time_to_live()
    {
        _plugin.TrendingResults.Add(Remote("global"));
        _plugin.RegionalTrendingResults["CO"] = [Remote("co")];
        await _feed.LoadAsync(HomeSectionKind.Trending);
        await _feed.LoadAsync(HomeSectionKind.TrendingColombia);

        Assert.Equal("global", Assert.Single(_cache.Entries[HomeSectionKind.Trending].Tracks).RemoteId);
        Assert.Equal("co", Assert.Single(_cache.Entries[HomeSectionKind.TrendingColombia].Tracks).RemoteId);

        _clock.Now = Start + GetHomeFeedUseCase.TrendingTimeToLive - TimeSpan.FromMinutes(1);
        await _feed.LoadAsync(HomeSectionKind.TrendingColombia);
        Assert.Equal(1, _plugin.TrendingRegionCalls.Count(r => r == "CO"));

        _clock.Now = Start + GetHomeFeedUseCase.TrendingTimeToLive;
        await _feed.LoadAsync(HomeSectionKind.TrendingColombia);
        Assert.Equal(2, _plugin.TrendingRegionCalls.Count(r => r == "CO"));
    }

    [Fact]
    public async Task Decorated_source_forwards_the_region_through_the_throttle_and_resilience_decorators()
    {
        var throttle = new RequestThrottle("fake", new ThrottleOptions());
        var source = (IDiscoverableMusicSource)NetworkResilientMusicSource.Wrap(
            ThrottledMusicSource.Wrap(_plugin, throttle), new AlwaysOnlineNetworkMonitor(), new ResilienceOptions());
        _plugin.RegionalTrendingResults["CO"] = [Remote("co")];

        Assert.Equal("co", Assert.Single(await source.GetTrendingAsync("CO", 5)).RemoteId);
        Assert.Empty(await source.GetTrendingAsync("ZZ", 5));
        Assert.Equal(["CO", "ZZ"], _plugin.TrendingRegionCalls);
    }

    [Fact]
    public async Task View_model_shows_trending_colombia_with_ranks_after_the_global_one()
    {
        _plugin.TrendingResults.Add(Remote("global"));
        _plugin.RegionalTrendingResults["CO"] = [Remote("co1"), Remote("co2")];
        var viewModel = CreateViewModel();

        await viewModel.OnAppearingAsync();

        Assert.True(viewModel.HasTrendingColombia);
        Assert.Equal(["co1", "co2"], viewModel.TrendingColombia.Select(r => r.Track.RemoteId));
        Assert.Equal([1, 2], viewModel.TrendingColombia.Select(r => r.Rank));
        Assert.Equal("global", Assert.Single(viewModel.Trending).Track.RemoteId);
    }

    [Fact]
    public async Task View_model_trending_colombia_failure_is_isolated_and_retry_reloads_only_it()
    {
        _plugin.TrendingResults.Add(Remote("global"));
        _plugin.RegionalTrendingErrors["CO"] = new HttpRequestException("offline");
        var viewModel = CreateViewModel();

        await viewModel.OnAppearingAsync();

        Assert.True(viewModel.HasTrendingColombiaError);
        Assert.True(viewModel.HasTrending);
        Assert.True(viewModel.HasNoForYou);

        _plugin.RegionalTrendingErrors.Clear();
        _plugin.RegionalTrendingResults["CO"] = [Remote("co")];
        await viewModel.RetryTrendingColombiaCommand.ExecuteAsync(null);

        Assert.True(viewModel.HasTrendingColombia);
        Assert.Equal(1, _plugin.TrendingCalls);
    }

    [Fact]
    public async Task View_model_refresh_fetches_trending_colombia_too()
    {
        _plugin.RegionalTrendingResults["CO"] = [Remote("co")];
        var viewModel = CreateViewModel();
        await viewModel.OnAppearingAsync();

        await viewModel.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(2, _plugin.TrendingRegionCalls.Count(r => r == "CO"));
    }

    private sealed class GatedCache(InMemoryHomeFeedCache inner, Task gate) : IHomeFeedCache
    {
        public async Task<HomeFeedEntry?> GetAsync(HomeSectionKind kind, CancellationToken cancellationToken = default)
        {
            await gate.WaitAsync(cancellationToken);
            return await inner.GetAsync(kind, cancellationToken);
        }

        public Task SetAsync(HomeSectionKind kind, HomeFeedEntry entry, CancellationToken cancellationToken = default) =>
            inner.SetAsync(kind, entry, cancellationToken);
    }

    private sealed class StreamOnlyPlugin : IStreamableMusicSource
    {
        public PluginInfo Info { get; } = new("stream-only", "Stream only", "x", "#000000");

        public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteTrack>>([]);

        public Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenStreamAsync(RemoteTrack track, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
