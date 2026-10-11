using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Import;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public class ExportifyCsvParserTests
{
    [Fact]
    public void Reads_quoted_fields_multiple_artists_and_duration()
    {
        const string csv = "﻿Track URI,Track Name,Album Name,Artist Name(s),Duration (ms),Genres\r\n"
            + "spotify:track:1,\"Tuya, Y Más Que Tuya\",\"Boleros\",\"Celia Cruz;La Sonora Matancera\",162133,\"salsa,son\"\r\n"
            + "spotify:track:2,\"Say \"\"Hi\"\"\",\"Multi\nline\",\"Sia\",217746,\"\"\r\n"
            + "spotify:local:3,\"\",\"\",\"\",0,\"\"\r\n";

        var playlist = ExportifyCsvParser.Parse(new StringReader(csv), "Liked Songs");

        Assert.Equal(2, playlist.Tracks.Count);
        Assert.Equal(new ImportedTrack("Tuya, Y Más Que Tuya", ["Celia Cruz", "La Sonora Matancera"], "Boleros", TimeSpan.FromMilliseconds(162133)), playlist.Tracks[0], new TrackComparer());
        Assert.Equal("Say \"Hi\"", playlist.Tracks[1].Title);
        Assert.Equal("Multi\nline", playlist.Tracks[1].Album);
    }

    [Fact]
    public void Rejects_files_that_are_not_exportify_csv()
    {
        Assert.Throws<FormatException>(() => ExportifyCsvParser.Parse(new StringReader("Name,Phone\nAna,123"), "x"));
    }

    private sealed class TrackComparer : IEqualityComparer<ImportedTrack>
    {
        public bool Equals(ImportedTrack? x, ImportedTrack? y) =>
            x!.Title == y!.Title && x.Artists.SequenceEqual(y.Artists) && x.Album == y.Album && x.Duration == y.Duration;

        public int GetHashCode(ImportedTrack obj) => obj.Title.GetHashCode();
    }
}

public class TrackMatcherTests
{
    private static readonly ImportedTrack Unstoppable = new("Unstoppable", ["Sia"], null, TimeSpan.FromSeconds(217));

    [Fact]
    public void Prefers_the_official_audio_with_matching_duration()
    {
        RemoteTrack[] candidates =
        [
            Remote("Sia - Unstoppable (Live at the Grammys)", "Sia", 240),
            Remote("Unstoppable - Sia (Karaoke Version)", "KaraokeFun", 217),
            Remote("Sia - Unstoppable (Official Video)", "SiaVEVO", 245),
            Remote("Unstoppable", "Sia - Topic", 218),
        ];

        Assert.Equal("Sia - Topic", TrackMatcher.BestMatch(Unstoppable, candidates)?.Artist);
    }

    [Fact]
    public void Accepts_a_music_video_when_there_is_no_official_audio()
    {
        RemoteTrack[] candidates = [Remote("Sia - Unstoppable (Official Video)", "SiaVEVO", 230), Remote("Sia - Chandelier", "SiaVEVO", 217)];

        Assert.Equal("Sia - Unstoppable (Official Video)", TrackMatcher.BestMatch(Unstoppable, candidates)?.Title);
    }

    [Fact]
    public void Rejects_unrelated_results()
    {
        RemoteTrack[] candidates = [Remote("Sia - Chandelier", "SiaVEVO", 217), Remote("Unstoppable reaction!!", "Random", 600)];

        Assert.Null(TrackMatcher.BestMatch(Unstoppable, candidates));
    }

    [Fact]
    public void Keeps_live_versions_when_the_original_is_live()
    {
        var live = new ImportedTrack("Hotel California - Live", ["Eagles"], null, TimeSpan.FromSeconds(431));

        var match = TrackMatcher.BestMatch(live, [Remote("Eagles - Hotel California (Live 1994)", "Eagles", 432)]);

        Assert.NotNull(match);
    }

    [Theory]
    [InlineData("Since I've Been Loving You - Remaster", "Since I've Been Loving You")]
    [InlineData("Latinoamérica (feat. Totó la Momposina)", "Latinoamérica")]
    [InlineData("Hotel California - Live", "Hotel California - Live")]
    public void Cleans_spotify_title_suffixes(string title, string expected) =>
        Assert.Equal(expected, TrackMatcher.CleanTitle(title));

    private static RemoteTrack Remote(string title, string channel, int seconds) =>
        new("demo", Guid.NewGuid().ToString("N"), title, channel, TimeSpan.FromSeconds(seconds), null);
}

public sealed class PlaylistImportServiceTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly SearchablePlugin _plugin = new();
    private readonly InMemoryLibrary _library = new();

    [Fact]
    public async Task Downloads_matches_and_fills_playlist_and_likes_in_file_order()
    {
        var (service, playlists, likes) = Create();
        var imported = new ImportedPlaylist("Liked Songs",
        [
            new ImportedTrack("Uno", ["A"], null, TimeSpan.FromSeconds(200)),
            new ImportedTrack("Inexistente", ["B"], null, TimeSpan.FromSeconds(200)),
            new ImportedTrack("Dos", ["A"], null, TimeSpan.FromSeconds(180)),
        ]);
        _plugin.Catalog.Add(new RemoteTrack("fake", "uno", "Uno", "A - Topic", TimeSpan.FromSeconds(200), null));
        _plugin.Catalog.Add(new RemoteTrack("fake", "dos", "Dos", "A - Topic", TimeSpan.FromSeconds(181), null));
        _plugin.DownloadDelays["uno"] = 300; // Finishes last: order must still follow the file.

        var session = await service.StartAsync(imported, new ImportOptions("fake", CreatePlaylist: true, AddToLikes: true));
        var finished = await WaitUntilFinishedAsync(service, session.Id);

        Assert.Equal((2, 1), (finished.Done, finished.NotFound));
        var playlist = await playlists.FindAsync(session.PlaylistId!.Value);
        Assert.Equal(["Uno", "Dos"], (await playlists.GetTracksAsync(playlist!)).Select(t => t.Title));
        await likes.InitializeAsync();
        Assert.Equal(2, likes.Count);
    }

    [Fact]
    public async Task Tracks_already_in_the_library_are_reused_without_searching()
    {
        var (service, playlists, _) = Create();
        var path = Path.Combine(_paths.MusicDirectory, "A - Uno [fake-old].m4a");
        Directory.CreateDirectory(_paths.MusicDirectory);
        await File.WriteAllTextAsync(path, "audio");
        var existing = new LocalTrack(Guid.NewGuid(), "Uno (Official Video)", "A", TimeSpan.FromSeconds(201), null, path, "fake", "old", DateTimeOffset.Now);
        _library.Tracks.Add(existing);
        _plugin.Catalog.Add(new RemoteTrack("fake", "uno", "Uno", "A - Topic", TimeSpan.FromSeconds(200), null));

        var session = await service.StartAsync(
            new ImportedPlaylist("L", [new ImportedTrack("Uno", ["A"], null, TimeSpan.FromSeconds(200))]),
            new ImportOptions("fake", CreatePlaylist: true, AddToLikes: false));
        var finished = await WaitUntilFinishedAsync(service, session.Id);

        Assert.Equal((1, 1), (finished.Done, finished.AlreadyInLibrary));
        Assert.Equal(0, _plugin.Searches);
        Assert.Single(_library.Tracks); // Nothing downloaded again.
        var playlist = await playlists.FindAsync(session.PlaylistId!.Value);
        Assert.Equal([existing.Id], playlist!.TrackIds);
    }

    [Fact]
    public void A_same_titled_song_by_another_artist_is_not_reused()
    {
        var mine = new LocalTrack(Guid.NewGuid(), "Hoy", "Gloria Estefan", TimeSpan.FromSeconds(240), null, "x.m4a", "fake", "1", DateTimeOffset.Now);

        Assert.Null(TrackMatcher.BestLocalMatch(new ImportedTrack("Hoy", ["Jennifer Lopez"], null, TimeSpan.FromSeconds(240)), [mine]));
        Assert.Same(mine, TrackMatcher.BestLocalMatch(new ImportedTrack("Hoy", ["Gloria Estefan"], null, TimeSpan.FromSeconds(241)), [mine]));
    }

    [Fact]
    public async Task Interrupted_imports_resume_after_restart()
    {
        var repository = new JsonImportSessionRepository(_paths);
        var track = new ImportedTrack("Uno", ["A"], null, TimeSpan.FromSeconds(200));
        _plugin.Catalog.Add(new RemoteTrack("fake", "uno", "Uno", "A - Topic", TimeSpan.FromSeconds(200), null));
        await repository.SaveAsync(new PlaylistImportSession(Guid.NewGuid(), "x", "fake", null, false, DateTimeOffset.Now, [new ImportItem(0, track)]));

        var (service, _, _) = Create(repository);
        await service.ResumeAsync();
        var finished = await WaitUntilFinishedAsync(service, (await repository.GetAllAsync())[0].Id);

        Assert.Equal(1, finished.Done);
        Assert.Single(_library.Tracks);
    }

    private (PlaylistImportService, PlaylistService, LikesService) Create(IImportSessionRepository? repository = null)
    {
        var catalog = new PluginCatalog([_plugin]);
        var clock = new FixedClock(DateTimeOffset.Now);
        var downloads = new DownloadQueue(new DownloadTrackUseCase(catalog, _library, _paths, clock));
        var playlists = new PlaylistService(new JsonPlaylistRepository(_paths), _library, clock);
        var likes = new LikesService(new JsonLikedTracksRepository(_paths), _library);
        var service = new PlaylistImportService(
            repository ?? new JsonImportSessionRepository(_paths), new SearchTracksUseCase(catalog), downloads, playlists, likes, _library, clock);
        return (service, playlists, likes);
    }

    private static async Task<PlaylistImportSession> WaitUntilFinishedAsync(PlaylistImportService service, Guid id)
    {
        PlaylistImportSession? session = null;
        await TestWait.UntilAsync(() =>
        {
            session = service.GetSessionsAsync().GetAwaiter().GetResult().Single(s => s.Id == id);
            return !session.IsRunning;
        });

        // Playlist and likes are updated right after the last item is marked done.
        await Task.Delay(200);
        return session!;
    }

    public void Dispose() => _paths.Dispose();

    /// <summary>Searches by title over a fixed catalog; downloads can be delayed per track.</summary>
    private sealed class SearchablePlugin : IMusicSourcePlugin
    {
        public PluginInfo Info { get; } = new("fake", "Fake", "", "#000000");

        public List<RemoteTrack> Catalog { get; } = [];

        public Dictionary<string, int> DownloadDelays { get; } = [];

        public int Searches { get; private set; }

        public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
        {
            Searches++;
            return Task.FromResult<IReadOnlyList<RemoteTrack>>(Catalog.Where(t => query.Contains(t.Title, StringComparison.OrdinalIgnoreCase)).ToList());
        }

        public async Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            await Task.Delay(DownloadDelays.GetValueOrDefault(track.RemoteId, 10), cancellationToken);
            var path = Path.Combine(destinationDirectory, $"{track.RemoteId}.m4a");
            await File.WriteAllTextAsync(path, "audio", cancellationToken);
            return path;
        }
    }
}
