using Wired.Core.Abstractions;
using System.Net;
using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Enrichment;
using Wired.Core.Import;
using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Core.Plugins;
using Wired.Core.Storage;
using Wired.Core.Throttling;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Infrastructure.Media;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class EnrichmentTests : IDisposable
{
    private readonly TempPaths _paths = new();

    [Fact]
    public void Tags_round_trip_title_artists_album_duration_and_cover_in_m4a()
    {
        var cover = File.ReadAllBytes(Fixture("cover.jpg"));
        var file = CopyFixture("silence.m4a", ScratchDirectory, "round.m4a");
        var tagger = new TagLibAudioTagger();

        tagger.Write(file, "Round Title", ["Artist One", "Artist Two"], "Round Album", cover);
        var tags = tagger.Read(file);

        Assert.NotNull(tags);
        Assert.Equal("Round Title", tags.Title);
        Assert.Equal(["Artist One", "Artist Two"], tags.Artists);
        Assert.Equal("Round Album", tags.Album);
        Assert.InRange(tags.Duration!.Value.TotalSeconds, 0.8, 1.2);
        Assert.Equal(cover, tags.Cover);
    }

    [Fact]
    public void Matroska_round_trip_keeps_the_artists()
    {
        var file = CopyFixture("silence.mka", ScratchDirectory, "round.mka");
        var tagger = new TagLibAudioTagger();

        tagger.Write(file, "Mka Title", ["Mka One", "Mka Two"], "Mka Album", File.ReadAllBytes(Fixture("cover.jpg")));
        var tags = tagger.Read(file);

        Assert.NotNull(tags);
        Assert.Equal(["Mka One", "Mka Two"], tags.Artists);
    }

    [Fact]
    public void Matroska_round_trip_keeps_the_title_by_not_writing_the_album()
    {
        // Regression: TagLib# shares one TITLE field for Matroska album and track titles.
        var file = CopyFixture("silence.mka", ScratchDirectory, "title.mka");
        var tagger = new TagLibAudioTagger();

        tagger.Write(file, "Mka Title", ["Mka One"], "Mka Album", null);

        var tags = tagger.Read(file)!;
        Assert.Equal("Mka Title", tags.Title);
        Assert.Null(tags.Album);
    }

    [Fact]
    public void Reading_a_file_without_metadata_returns_null()
    {
        var file = CopyFixture("silence.m4a", ScratchDirectory, "untagged.m4a");

        Assert.Null(new TagLibAudioTagger().Read(file));
    }

    [Fact]
    public async Task Artwork_is_saved_from_url_and_found_by_key()
    {
        var cover = File.ReadAllBytes(Fixture("cover.jpg"));
        var store = CreateArtwork(new BytesHandler(cover));

        var path = await store.SaveFromUrlAsync("fake_x", "https://img.test/cover.jpg");

        Assert.NotNull(path);
        Assert.Equal(path, store.Find("fake_x"));
        Assert.Equal(cover, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Artwork_that_cannot_be_fetched_returns_null_and_stores_nothing()
    {
        var store = CreateArtwork(new BytesHandler([], HttpStatusCode.NotFound));

        var path = await store.SaveFromUrlAsync("fake_gone", "https://img.test/gone.jpg");

        Assert.Null(path);
        Assert.Null(store.Find("fake_gone"));
        var directory = Path.Combine(_paths.DataDirectory, "artwork");
        Assert.True(!Directory.Exists(directory) || Directory.GetFiles(directory).Length == 0);
    }

    [Fact]
    public async Task Enrich_saves_the_cover_and_writes_title_and_cover_into_the_file()
    {
        var cover = File.ReadAllBytes(Fixture("cover.jpg"));
        var file = CopyFixture("silence.m4a", _paths.MusicDirectory, "Artist - Song [fake-x].m4a");
        var library = new InMemoryLibrary();
        var track = Track(file, thumbnail: "https://img.test/cover.jpg");
        library.Tracks.Add(track);
        var service = CreateService(library, new BytesHandler(cover));
        var updates = Capture(service);

        await service.EnrichAsync(track.Id);

        var updated = Assert.Single(library.Tracks);
        Assert.True(updated.IsEnriched);
        Assert.NotNull(updated.ArtworkPath);
        Assert.True(File.Exists(updated.ArtworkPath));
        Assert.Equal(track.Id, Assert.Single(updates).Id);

        var tags = new TagLibAudioTagger().Read(file);
        Assert.Equal("Song", tags!.Title);
        Assert.Equal(cover, tags.Cover);
    }

    [Fact]
    public async Task Apply_imported_updates_library_and_file_tags()
    {
        var file = CopyFixture("silence.m4a", _paths.MusicDirectory, "Artist - Song [fake-x].m4a");
        var library = new InMemoryLibrary();
        var track = Track(file);
        library.Tracks.Add(track);
        var service = CreateService(library, new BytesHandler([]));

        await service.ApplyImportedAsync(track.Id, new ImportedTrack("Real Title", ["A", "B"], "Album", null));

        var updated = Assert.Single(library.Tracks);
        Assert.Equal("Real Title", updated.Title);
        Assert.Equal("A, B", updated.Artist);
        Assert.Equal("Album", updated.Album);
        Assert.True(updated.IsEnriched);

        var tags = new TagLibAudioTagger().Read(file);
        Assert.Equal("Real Title", tags!.Title);
        Assert.Equal(["A", "B"], tags.Artists);
        Assert.Equal("Album", tags.Album);
    }

    [Fact]
    public async Task Backfill_applies_finished_imports_and_enriches_then_changes_nothing_on_second_run()
    {
        var library = new InMemoryLibrary();
        var imported = Track(CopyFixture("silence.m4a", _paths.MusicDirectory, "Old - Old [fake-imp].m4a"), "Old Title", "Old Artist", remoteId: "imp");
        var plain = Track(
            CopyFixture("silence.m4a", _paths.MusicDirectory, "Plain - Plain [fake-plain].m4a"),
            "Plain", "Plain Artist", thumbnail: "https://img.test/plain.jpg", remoteId: "plain") with { IsEnriched = false };
        library.Tracks.Add(imported);
        library.Tracks.Add(plain);

        var sessions = new JsonImportSessionRepository(_paths);
        var item = new ImportItem(0, new ImportedTrack("Real Title", ["A", "B"], "Album", null), ImportItemStatus.Done, null, imported.Id);
        await sessions.SaveAsync(new PlaylistImportSession(Guid.NewGuid(), "Import", "fake", null, false, DateTimeOffset.Now, [item]));

        var cover = File.ReadAllBytes(Fixture("cover.jpg"));
        var service = CreateService(library, new BytesHandler(cover), sessions);
        var updates = Capture(service);

        await service.BackfillAsync();

        var afterFirst = library.Tracks.Single(t => t.Id == imported.Id);
        Assert.Equal("Real Title", afterFirst.Title);
        Assert.Equal("A, B", afterFirst.Artist);
        Assert.Equal("Album", afterFirst.Album);
        Assert.True(afterFirst.IsEnriched);
        Assert.Equal("Real Title", new TagLibAudioTagger().Read(imported.FilePath)!.Title);

        var plainAfter = library.Tracks.Single(t => t.Id == plain.Id);
        Assert.True(plainAfter.IsEnriched);
        Assert.NotNull(plainAfter.ArtworkPath);
        Assert.True(File.Exists(plainAfter.ArtworkPath));

        var countAfterFirst = updates.Count;
        Assert.True(countAfterFirst > 0);

        await service.BackfillAsync();

        Assert.Equal(countAfterFirst, updates.Count);
    }

    [Fact]
    public async Task Recover_uses_the_tags_for_title_artists_album_duration_and_cover()
    {
        var cover = File.ReadAllBytes(Fixture("cover.jpg"));
        var name = MusicFileNaming.FileNameFor("File Title", "File Artist", "fake", "rec1", ".m4a");
        var file = CopyFixture("silence.m4a", _paths.MusicDirectory, name);
        new TagLibAudioTagger().Write(file, "Tag Title", ["X", "Y"], "Tag Album", cover);
        var library = new InMemoryLibrary();
        var artwork = CreateArtwork(new BytesHandler([]));

        var recovered = await new RecoverMusicUseCase(library, _paths, new TagLibAudioTagger(), artwork).ExecuteAsync();

        Assert.Equal(1, recovered);
        var track = Assert.Single(library.Tracks);
        Assert.Equal("Tag Title", track.Title);
        Assert.Equal("X, Y", track.Artist);
        Assert.Equal("Tag Album", track.Album);
        Assert.NotNull(track.Duration);
        Assert.InRange(track.Duration!.Value.TotalSeconds, 0.8, 1.2);
        Assert.True(track.IsEnriched);
        Assert.NotNull(track.ArtworkPath);
        Assert.Equal(cover, File.ReadAllBytes(track.ArtworkPath!));
    }

    [Fact]
    public async Task Recover_falls_back_to_the_file_name_when_untagged()
    {
        var name = MusicFileNaming.FileNameFor("Plain Title", "Plain Artist", "fake", "plain1", ".m4a");
        CopyFixture("silence.m4a", _paths.MusicDirectory, name);
        var library = new InMemoryLibrary();

        var recovered = await new RecoverMusicUseCase(library, _paths, new TagLibAudioTagger(), CreateArtwork(new BytesHandler([])))
            .ExecuteAsync();

        Assert.Equal(1, recovered);
        var track = Assert.Single(library.Tracks);
        Assert.Equal("Plain Title", track.Title);
        Assert.Equal("Plain Artist", track.Artist);
        Assert.Null(track.Album);
    }

    [Fact]
    public async Task Recover_does_not_mark_an_untagged_file_as_enriched()
    {
        var name = MusicFileNaming.FileNameFor("Plain Title", "Plain Artist", "fake", "plain2", ".m4a");
        CopyFixture("silence.m4a", _paths.MusicDirectory, name);
        var library = new InMemoryLibrary();

        await new RecoverMusicUseCase(library, _paths, new TagLibAudioTagger(), CreateArtwork(new BytesHandler([])))
            .ExecuteAsync();

        Assert.False(Assert.Single(library.Tracks).IsEnriched);
    }

    [Fact]
    public async Task Recovered_music_gets_the_cover_its_source_builds_from_the_id_and_its_duration()
    {
        var cover = File.ReadAllBytes(Fixture("cover.jpg"));
        var name = MusicFileNaming.FileNameFor("Plain Title", "Plain Artist", "fake", "plain3", ".m4a");
        CopyFixture("silence.m4a", _paths.MusicDirectory, name);
        var library = new InMemoryLibrary();
        await new RecoverMusicUseCase(library, _paths, new TagLibAudioTagger(), CreateArtwork(new BytesHandler([]))).ExecuteAsync();
        var recovered = Assert.Single(library.Tracks);
        Assert.Null(recovered.ThumbnailUrl);
        Assert.Null(recovered.Duration);

        await CreateService(library, new BytesHandler(cover), plugin: new CoverPlugin()).BackfillAsync();

        var enriched = Assert.Single(library.Tracks);
        Assert.Equal("https://covers.test/plain3.jpg", enriched.ThumbnailUrl);
        Assert.NotNull(enriched.ArtworkPath);
        Assert.Equal(cover, await File.ReadAllBytesAsync(enriched.ArtworkPath));
        Assert.InRange(enriched.Duration!.Value.TotalSeconds, 0.8, 1.2);
    }

    [Fact]
    public async Task Backfill_gives_every_track_its_cover_before_measuring_loudness()
    {
        var cover = File.ReadAllBytes(Fixture("cover.jpg"));
        var library = new InMemoryLibrary();
        foreach (var id in new[] { "one", "two", "three" })
        {
            var name = MusicFileNaming.FileNameFor($"Title {id}", "Artist", "fake", id, ".m4a");
            CopyFixture("silence.m4a", _paths.MusicDirectory, name);
        }

        await new RecoverMusicUseCase(library, _paths, new TagLibAudioTagger(), CreateArtwork(new BytesHandler([]))).ExecuteAsync();
        var coversWhenMeasuring = new List<int>();
        var analyzer = new SpyAnalyzer(() => coversWhenMeasuring.Add(library.Tracks.Count(t => t.ArtworkPath is not null)));

        await CreateService(library, new BytesHandler(cover), plugin: new CoverPlugin(), loudness: analyzer).BackfillAsync();

        Assert.Equal([3, 3, 3], coversWhenMeasuring);
        Assert.All(library.Tracks, t => Assert.Equal(-20, t.LoudnessDb));
    }

    [Fact]
    public void Decorated_sources_still_build_covers_from_ids()
    {
        IMusicSourcePlugin plugin = new CoverPlugin();
        var decorated = new RequestThrottleRegistry(new ThrottleOptions())
            .Wrap([plugin])
            .Select(p => Wired.Core.Resilience.NetworkResilientMusicSource.Wrap(p, new AlwaysOnlineNetworkMonitor(), new Wired.Core.Resilience.ResilienceOptions()))
            .Single();

        Assert.Equal("https://covers.test/abc.jpg", new PluginCatalog([decorated]).Get("fake").ThumbnailUrlFor("abc"));
    }

    [Fact]
    public async Task The_song_that_starts_playing_gets_its_cover_right_away()
    {
        var cover = File.ReadAllBytes(Fixture("cover.jpg"));
        var name = MusicFileNaming.FileNameFor("Playing", "Artist", "fake", "playing1", ".m4a");
        CopyFixture("silence.m4a", _paths.MusicDirectory, name);
        var library = new InMemoryLibrary();
        await new RecoverMusicUseCase(library, _paths, new TagLibAudioTagger(), CreateArtwork(new BytesHandler([]))).ExecuteAsync();
        var playback = TestPlayback.Create(new FakeAudioPlayer());
        new PlayingTrackArtwork(playback, CreateService(library, new BytesHandler(cover), plugin: new CoverPlugin())).Start();

        await playback.PlayAsync([PlaybackItem.FromLocal(Assert.Single(library.Tracks))]);

        await TestWait.UntilAsync(() => playback.CurrentItem?.LocalTrack?.ArtworkPath is not null);
        Assert.Equal(playback.CurrentItem!.LocalTrack!.ArtworkPath, playback.CurrentItem.ThumbnailUrl);
        Assert.NotNull(Assert.Single(library.Tracks).ArtworkPath);
    }

    [Fact]
    public async Task Download_cleans_video_titles_and_artist_channels()
    {
        var library = new InMemoryLibrary();
        var useCase = new DownloadTrackUseCase(new PluginCatalog([new FakePlugin()]), library, _paths, new FixedClock(DateTimeOffset.UnixEpoch));

        var local = await useCase.ExecuteAsync(
            new RemoteTrack("fake", "x", "Sia - Unstoppable (Official Video)", "SiaVEVO", null, null));

        Assert.Equal("Unstoppable", local.Title);
        Assert.Equal("Sia", local.Artist);
    }

    private string ScratchDirectory => Path.Combine(_paths.CacheDirectory, "scratch");

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>Copies a fixture into a scratch folder so tests can modify it freely.</summary>
    private static string CopyFixture(string fixture, string directory, string fileName)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, fileName);
        File.Copy(Fixture(fixture), target, overwrite: true);
        return target;
    }

    private static LocalTrack Track(string filePath, string title = "Song", string artist = "Artist", string? thumbnail = null, string remoteId = "x") => new(
        Guid.NewGuid(), title, artist, TimeSpan.FromSeconds(1), thumbnail, filePath, "fake", remoteId, DateTimeOffset.UnixEpoch);

    private FileArtworkStore CreateArtwork(HttpMessageHandler http) => new(_paths, new HttpClient(http));

    private static List<LocalTrack> Capture(LibraryEnrichmentService service)
    {
        var updates = new List<LocalTrack>();
        service.TrackUpdated += (_, track) => updates.Add(track);
        return updates;
    }

    private LibraryEnrichmentService CreateService(InMemoryLibrary library, HttpMessageHandler http, IImportSessionRepository? sessions = null, IMusicSourcePlugin? plugin = null, ILoudnessAnalyzer? loudness = null)
    {
        var catalog = new PluginCatalog([plugin ?? new FakePlugin()]);
        var clock = new FixedClock(DateTimeOffset.Now);
        var downloads = new DownloadQueue(new DownloadTrackUseCase(catalog, library, _paths, clock));
        var playlists = new PlaylistService(new JsonPlaylistRepository(_paths), library, clock);
        var likes = new LikesService(new JsonLikedTracksRepository(_paths), library);
        var imports = new PlaylistImportService(
            sessions ?? new JsonImportSessionRepository(_paths), new SearchTracksUseCase(catalog), downloads, playlists, likes, library, clock);
        return new LibraryEnrichmentService(library, CreateArtwork(http), new TagLibAudioTagger(), downloads, imports, loudness, catalog);
    }

    private sealed class SpyAnalyzer(Action onMeasure) : ILoudnessAnalyzer
    {
        public Task<double?> MeasureAsync(string filePath, CancellationToken cancellationToken = default)
        {
            onMeasure();
            return Task.FromResult<double?>(-20);
        }
    }

    /// <summary>A source that knows the cover of any track from its id.</summary>
    private sealed class CoverPlugin : IStreamableMusicSource
    {
        private readonly FakePlugin _inner = new();

        public Task<Stream> OpenStreamAsync(RemoteTrack track, CancellationToken cancellationToken = default) =>
            _inner.OpenStreamAsync(track, cancellationToken);

        public PluginInfo Info => _inner.Info;

        public string? ThumbnailUrlFor(string remoteId) => $"https://covers.test/{remoteId}.jpg";

        public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
            _inner.SearchAsync(query, maxResults, cancellationToken);

        public Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            _inner.DownloadAsync(track, destinationDirectory, progress, cancellationToken);
    }

    public void Dispose() => _paths.Dispose();

    /// <summary>Answers every request with the given body, or with the given status and no body.</summary>
    private sealed class BytesHandler(byte[] body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }
}
