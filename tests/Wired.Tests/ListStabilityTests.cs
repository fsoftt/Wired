using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Enrichment;
using Wired.Core.Import;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Presentation.Collections;
using Wired.Presentation.Services;
using Wired.Presentation.ViewModels;
using Wired.Tests.Fakes;

namespace Wired.Tests;

/// <summary>
/// Regression tests for the Library list jumping back to the top on background updates:
/// bound collections must change by minimal moves/inserts/removes (never Reset), and rows must keep their instances.
/// </summary>
public sealed class ListStabilityTests : IDisposable
{
    private readonly TempPaths _paths = new();

    public void Dispose() => _paths.Dispose();

    private sealed class Row(int id)
    {
        public int Id { get; } = id;

        public override string ToString() => $"Row {Id}";
    }

    private static List<NotifyCollectionChangedEventArgs> Record<T>(ObservableCollection<T> collection)
    {
        var events = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, e) => events.Add(e);
        return events;
    }

    private static void AssertNoReset(IEnumerable<NotifyCollectionChangedEventArgs> events) =>
        Assert.DoesNotContain(events, e => e.Action == NotifyCollectionChangedAction.Reset);

    private static void AssertSameContent<T>(IEnumerable<T> actual, IReadOnlyList<T> expected)
        where T : class
    {
        var actualList = actual.ToList();
        Assert.Equal(expected.Count, actualList.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.True(ReferenceEquals(expected[i], actualList[i]), $"Position {i}: expected {expected[i]}, got {actualList[i]}");
    }

    // ---- 1. SyncTo basics ----

    [Fact]
    public void SyncTo_moves_removes_and_inserts_without_reset_and_keeps_row_instances()
    {
        var a = new Row(1);
        var b = new Row(2);
        var c = new Row(3);
        var d = new Row(4);
        var collection = new ObservableCollection<Row>([a, b, c]);
        var events = Record(collection);

        collection.SyncTo([c, a, d]);

        AssertSameContent(collection, [c, a, d]);
        AssertNoReset(events);
        Assert.True(ReferenceEquals(a, collection[1]));
        Assert.True(ReferenceEquals(c, collection[0]));
    }

    // ---- 2. Duplicates removed/added and empty targets ----

    [Fact]
    public void SyncTo_an_empty_target_removes_everything_without_reset()
    {
        var collection = new ObservableCollection<Row>([new Row(1), new Row(2), new Row(3)]);
        var events = Record(collection);

        collection.SyncTo([]);

        Assert.Empty(collection);
        AssertNoReset(events);
    }

    [Fact]
    public void SyncTo_from_empty_adds_everything_without_reset()
    {
        var collection = new ObservableCollection<Row>();
        var events = Record(collection);
        var rows = new[] { new Row(1), new Row(2) };

        collection.SyncTo(rows);

        AssertSameContent(collection, rows);
        AssertNoReset(events);
    }

    [Fact]
    public void SyncTo_removing_and_re_adding_the_same_instance_keeps_it_and_leaves_no_duplicates()
    {
        var a = new Row(1);
        var b = new Row(2);
        var collection = new ObservableCollection<Row>([a, b]);
        var events = Record(collection);

        collection.SyncTo([b]);
        collection.SyncTo([b, a]);

        AssertSameContent(collection, [b, a]);
        AssertNoReset(events);
    }

    // ---- 3. Seeded random property check ----

    [Fact]
    public void SyncTo_reaches_any_target_for_random_permutations_and_subsets()
    {
        var random = new Random(20260108);
        var pool = Enumerable.Range(0, 21).Select(i => new Row(i)).ToList();

        for (var iteration = 0; iteration < 200; iteration++)
        {
            // Starting content: random subset of the pool in random order.
            var start = pool.OrderBy(_ => random.Next()).Take(random.Next(0, 21)).ToList();

            // Target: random subset of the pool in random order, plus a few brand new rows.
            var target = pool.Where(_ => random.Next(2) == 0).ToList();
            target.AddRange(Enumerable.Range(0, random.Next(0, 4)).Select(i => new Row(1000 + iteration * 10 + i)));
            target = target.OrderBy(_ => random.Next()).ToList();

            var collection = new ObservableCollection<Row>(start);
            var events = Record(collection);

            collection.SyncTo(target);

            AssertSameContent(collection, target);
            Assert.True(events.All(e => e.Action != NotifyCollectionChangedAction.Reset),
                $"Iteration {iteration} raised Reset.");
        }
    }

    // ---- Fakes for LibraryViewModel construction ----

    private sealed class StabilityArtworkStore : IArtworkStore
    {
        public string? Find(string key) => null;

        public Task<string?> SaveFromUrlAsync(string key, string url, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string> SaveAsync(string key, byte[] image, CancellationToken cancellationToken = default) =>
            Task.FromResult(key);
    }

    private sealed class StabilityAudioTagger : IAudioTagger
    {
        public AudioTags? Read(string filePath) => null;

        public void Write(string filePath, string title, IReadOnlyList<string> artists, string? album, byte[]? cover)
        {
        }
    }

    private sealed class StabilityMusicFolder : IMusicFolderAccess
    {
        public string DisplayLocation => "Música/Wired";

        public bool FolderExists => false;

        public Task<bool> HasReadAccessAsync() => Task.FromResult(true);

        public Task<bool> RequestReadAccessAsync() => Task.FromResult(true);
    }

    private sealed class Harness
    {
        public required InMemoryLibrary Library { get; init; }
        public required LibraryViewModel ViewModel { get; init; }
        public required LibraryEnrichmentService Enrichment { get; init; }
        public required DownloadQueue Downloads { get; init; }
    }

    private Harness CreateLibrary(params LocalTrack[] tracks)
    {
        var library = new InMemoryLibrary();
        library.Tracks.AddRange(tracks);

        var catalog = new PluginCatalog([new FakePlugin()]);
        var clock = new FixedClock(DateTimeOffset.Now);
        var downloads = new DownloadQueue(new DownloadTrackUseCase(catalog, library, _paths, clock));
        var playlistRepository = new JsonPlaylistRepository(_paths);
        var likedRepository = new JsonLikedTracksRepository(_paths);
        var playlists = new PlaylistService(playlistRepository, library, clock);
        var likes = new LikesService(likedRepository, library);
        var dialogs = new FakeDialogs();
        var playback = TestPlayback.Create(new FakeAudioPlayer(), new FakePlugin());
        var actions = new TrackCollectionActions(likes, playlists, new QueueActions(playback, dialogs), dialogs);
        var imports = new PlaylistImportService(
            new JsonImportSessionRepository(_paths), new SearchTracksUseCase(catalog), downloads, playlists, likes, library, clock);
        var enrichment = new LibraryEnrichmentService(library, new StabilityArtworkStore(), new StabilityAudioTagger(), downloads, imports);

        var viewModel = new LibraryViewModel(
            library,
            new RemoveTrackUseCase(library, playlists, likes),
            new RecoverMusicUseCase(library, _paths),
            new CollectionsBackupService(playlistRepository, playlists, likedRepository, likes, library, _paths),
            new StabilityMusicFolder(),
            playback,
            likes,
            actions,
            downloads,
            dialogs,
            new ImmediateDispatcher(),
            new CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger(),
            enrichment);

        return new Harness { Library = library, ViewModel = viewModel, Enrichment = enrichment, Downloads = downloads };
    }

    // ---- 4. Enrichment update keeps the row in place ----

    [Fact]
    public async Task Enrichment_update_of_one_track_changes_its_row_in_place_without_collection_changes()
    {
        var first = TestData.LocalTrack("First");
        var second = TestData.LocalTrack("Second");
        var third = TestData.LocalTrack("Third");
        var harness = CreateLibrary(first, second, third);
        await harness.ViewModel.LoadCommand.ExecuteAsync(null);
        var rowBefore = harness.ViewModel.Tracks.Single(t => t.Track.Id == second.Id);
        var rowsBefore = harness.ViewModel.Tracks.ToList();
        var events = Record(harness.ViewModel.Tracks);

        await harness.Enrichment.ApplyImportedAsync(second.Id, new ImportedTrack("New title", ["X"], null, null));

        Assert.Empty(events);
        AssertNoReset(events);
        AssertSameContent(harness.ViewModel.Tracks, rowsBefore);
        Assert.True(ReferenceEquals(rowBefore, harness.ViewModel.Tracks.Single(t => t.Track.Id == second.Id)));
        Assert.Equal("New title", rowBefore.Title);
    }

    // ---- 5. Completed download inserts one row at the top ----

    [Fact]
    public async Task Completed_download_inserts_exactly_one_row_at_the_top_without_reset()
    {
        var harness = CreateLibrary(TestData.LocalTrack("One"), TestData.LocalTrack("Two"), TestData.LocalTrack("Three"));
        await harness.ViewModel.LoadCommand.ExecuteAsync(null);
        var rowsBefore = harness.ViewModel.Tracks.ToList();
        var events = Record(harness.ViewModel.Tracks);

        harness.Downloads.Enqueue(TestData.RemoteTrack("new"));
        await TestWait.UntilAsync(() => harness.ViewModel.Tracks.Count == 4);

        AssertNoReset(events);
        var added = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Add, added.Action);
        Assert.Equal(0, added.NewStartingIndex);
        Assert.Single(added.NewItems!);
        Assert.Equal("new", ((TrackItemViewModel)added.NewItems![0]!).Track.RemoteId);
        AssertSameContent(harness.ViewModel.Tracks.Skip(1), rowsBefore);
    }

    // ---- 6. Filter keeps row instances ----

    [Fact]
    public async Task Filtering_down_and_back_never_resets_and_restores_the_same_rows()
    {
        var alpha = TestData.LocalTrack("Alpha");
        var beta = TestData.LocalTrack("Beta");
        var gamma = TestData.LocalTrack("Gamma");
        var harness = CreateLibrary(alpha, beta, gamma);
        await harness.ViewModel.LoadCommand.ExecuteAsync(null);
        var rowsBefore = harness.ViewModel.Tracks.ToList();
        var events = Record(harness.ViewModel.Tracks);

        harness.ViewModel.Filter = "alp";
        Assert.Single(harness.ViewModel.Tracks);
        Assert.True(ReferenceEquals(rowsBefore[0], harness.ViewModel.Tracks[0]));

        harness.ViewModel.Filter = string.Empty;

        AssertNoReset(events);
        AssertSameContent(harness.ViewModel.Tracks, rowsBefore);
    }
}
