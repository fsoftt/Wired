using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Import;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Tests.Fakes;

namespace Wired.Tests;

/// <summary>Choosing by hand the version of an imported track that was not found (or failed).</summary>
public sealed class ChooseImportMatchTests : IDisposable
{
    private static readonly ImportedTrack Uno = new("Uno", ["A"], null, TimeSpan.FromSeconds(200));
    private static readonly ImportedTrack Inexistente = new("Inexistente", ["B"], null, TimeSpan.FromSeconds(200));
    private static readonly ImportedTrack Dos = new("Dos", ["A"], null, TimeSpan.FromSeconds(180));

    private readonly TempPaths _paths = new();
    private readonly ChoosePlugin _plugin = new();
    private readonly InMemoryLibrary _library = new();

    private static RemoteTrack Chosen => new("fake", "inex", "Inexistente", "B - Topic", TimeSpan.FromSeconds(200), null);

    [Fact]
    public async Task Chosen_match_is_downloaded_and_placed_in_the_playlist_at_the_imported_position()
    {
        var (service, playlists, _, _) = Create();
        _plugin.Catalog.Add(new RemoteTrack("fake", "uno", "Uno", "A - Topic", TimeSpan.FromSeconds(200), null));
        _plugin.Catalog.Add(new RemoteTrack("fake", "dos", "Dos", "A - Topic", TimeSpan.FromSeconds(181), null));
        _plugin.DownloadDelays["inex"] = 300; // Finishes last: order must still follow the file.
        var session = await StartAsync(service, Uno, Inexistente, Dos);
        Assert.Equal(1, session.NotFound);

        await service.ChooseMatchAsync(session.Id, 1, Chosen);
        var finished = await WaitUntilFinishedAsync(service, session.Id);

        Assert.Equal(3, finished.Done);
        Assert.Equal(ImportItemStatus.Done, finished.Items[1].Status);
        Assert.NotNull(finished.Items[1].LocalTrackId);
        Assert.Null(finished.Items[1].Error);
        var playlist = await playlists.FindAsync(session.PlaylistId!.Value);
        Assert.Equal(["Uno", "Inexistente", "Dos"], (await playlists.GetTracksAsync(playlist!)).Select(t => t.Title));
    }

    [Fact]
    public async Task Not_found_item_becomes_downloading_with_the_match_and_no_error_while_the_download_runs()
    {
        var (service, _, _, downloads) = Create();
        _plugin.Gate = new TaskCompletionSource();
        var session = await StartAsync(service, Uno, Inexistente);

        await service.ChooseMatchAsync(session.Id, 1, Chosen);

        var current = await GetSessionAsync(service, session.Id);
        var item = current.Items[1];
        Assert.Equal(ImportItemStatus.Downloading, item.Status);
        Assert.Equal(Chosen, item.Match);
        Assert.Null(item.Error);
        Assert.Contains(downloads.Jobs, j => j.Track == Chosen);

        _plugin.Gate.SetResult();
        var finished = await WaitUntilFinishedAsync(service, session.Id);
        Assert.Equal(ImportItemStatus.Done, finished.Items[1].Status);
    }

    [Fact]
    public async Task Failed_item_can_be_chosen_and_its_error_is_cleared()
    {
        var (service, _, _, _) = Create();
        _plugin.FailingSearchTitles.Add("Inexistente");
        var session = await StartAsync(service, Uno, Inexistente);
        Assert.Equal(ImportItemStatus.Failed, session.Items[1].Status);
        Assert.NotNull(session.Items[1].Error);

        _plugin.Gate = new TaskCompletionSource();
        await service.ChooseMatchAsync(session.Id, 1, Chosen);

        var running = await GetSessionAsync(service, session.Id);
        Assert.Equal(ImportItemStatus.Downloading, running.Items[1].Status);
        Assert.Null(running.Items[1].Error);

        _plugin.Gate.SetResult();
        var finished = await WaitUntilFinishedAsync(service, session.Id);
        Assert.Equal(ImportItemStatus.Done, finished.Items[1].Status);
        Assert.Null(finished.Items[1].Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Chosen_track_is_liked_only_when_the_session_adds_to_likes(bool addToLikes)
    {
        var (service, _, likes, _) = Create();
        var session = await StartAsync(service, addToLikes, Uno, Inexistente);

        await service.ChooseMatchAsync(session.Id, 1, Chosen);
        var finished = await WaitUntilFinishedAsync(service, session.Id);

        await likes.InitializeAsync();
        var localId = finished.Items[1].LocalTrackId!.Value;
        Assert.Equal(addToLikes, likes.IsLiked(localId));
    }

    [Fact]
    public async Task Other_items_are_left_untouched()
    {
        var (service, _, _, _) = Create();
        _plugin.Catalog.Add(new RemoteTrack("fake", "uno", "Uno", "A - Topic", TimeSpan.FromSeconds(200), null));
        var session = await StartAsync(service, Uno, Inexistente, Dos);
        var before = await GetSessionAsync(service, session.Id);
        Assert.Equal(ImportItemStatus.Done, before.Items[0].Status);
        Assert.Equal(ImportItemStatus.NotFound, before.Items[2].Status);

        _plugin.Gate = new TaskCompletionSource();
        await service.ChooseMatchAsync(session.Id, 1, Chosen);
        var during = await GetSessionAsync(service, session.Id);
        Assert.Equal(Snapshot(before.Items[0]), Snapshot(during.Items[0]));
        Assert.Equal(Snapshot(before.Items[2]), Snapshot(during.Items[2]));

        _plugin.Gate.SetResult();
        var finished = await WaitUntilFinishedAsync(service, session.Id);
        Assert.Equal(Snapshot(before.Items[0]), Snapshot(finished.Items[0]));
        Assert.Equal(Snapshot(before.Items[2]), Snapshot(finished.Items[2]));
    }

    [Fact]
    public async Task Unknown_session_does_nothing()
    {
        var (service, _, _, downloads) = Create();
        var session = await StartAsync(service, Uno, Inexistente);
        var before = await GetSessionAsync(service, session.Id);

        await service.ChooseMatchAsync(Guid.NewGuid(), 1, Chosen);

        Assert.Empty(downloads.Jobs);
        Assert.Empty(_library.Tracks);
        Assert.Equal(before.Items.Select(Snapshot), (await GetSessionAsync(service, session.Id)).Items.Select(Snapshot));
    }

    [Fact]
    public async Task Chosen_track_already_in_the_library_ends_done_without_downloading_again()
    {
        var (service, playlists, _, _) = Create();
        var existing = new LocalTrack(Guid.NewGuid(), "Inexistente", "B", TimeSpan.FromSeconds(200), null, "/music/x.m4a", "fake", "inex", DateTimeOffset.Now);
        _library.Tracks.Add(existing);
        var session = await StartAsync(service, Inexistente);

        await service.ChooseMatchAsync(session.Id, 0, Chosen);
        var finished = await WaitUntilFinishedAsync(service, session.Id);

        Assert.Equal(ImportItemStatus.Done, finished.Items[0].Status);
        Assert.Equal(existing.Id, finished.Items[0].LocalTrackId);
        Assert.Equal(0, _plugin.DownloadCount);
        Assert.Single(_library.Tracks);
        var playlist = await playlists.FindAsync(session.PlaylistId!.Value);
        Assert.Equal([existing.Id], playlist!.TrackIds);
    }

    [Fact]
    public async Task Two_items_choosing_the_same_track_both_end_done()
    {
        var (service, _, _, _) = Create();
        var session = await StartAsync(service, Inexistente, new ImportedTrack("Inexistente", ["B"], null, TimeSpan.FromSeconds(200)));

        await service.ChooseMatchAsync(session.Id, 0, Chosen);
        await service.ChooseMatchAsync(session.Id, 1, Chosen);
        var finished = await WaitUntilFinishedAsync(service, session.Id);

        Assert.Equal(2, finished.Done);
    }

    private (PlaylistImportService Service, PlaylistService Playlists, LikesService Likes, DownloadQueue Downloads) Create()
    {
        var catalog = new PluginCatalog([_plugin]);
        var clock = new FixedClock(DateTimeOffset.Now);
        var downloads = new DownloadQueue(new DownloadTrackUseCase(catalog, _library, _paths, clock));
        var playlists = new PlaylistService(new JsonPlaylistRepository(_paths), _library, clock);
        var likes = new LikesService(new JsonLikedTracksRepository(_paths), _library);
        var service = new PlaylistImportService(
            new JsonImportSessionRepository(_paths), new SearchTracksUseCase(catalog), downloads, playlists, likes, _library, clock);
        return (service, playlists, likes, downloads);
    }

    private static Task<PlaylistImportSession> StartAsync(PlaylistImportService service, params ImportedTrack[] tracks) =>
        StartAsync(service, addToLikes: false, tracks);

    private static async Task<PlaylistImportSession> StartAsync(PlaylistImportService service, bool addToLikes, params ImportedTrack[] tracks)
    {
        var session = await service.StartAsync(
            new ImportedPlaylist("L", tracks),
            new ImportOptions("fake", CreatePlaylist: true, AddToLikes: addToLikes));
        // Searches are quick: wait until every item has a result (done, not found or failed).
        return await WaitUntilFinishedAsync(service, session.Id);
    }

    /// <summary>Value copy of an item: ImportedTrack.Artists is a list, so record equality would compare references.</summary>
    private static (int, ImportItemStatus, RemoteTrack?, Guid?, string?, bool, string, string) Snapshot(ImportItem item) =>
        (item.Index, item.Status, item.Match, item.LocalTrackId, item.Error, item.AlreadyInLibrary, item.Track.Title, string.Join("|", item.Track.Artists));

    private static async Task<PlaylistImportSession> GetSessionAsync(PlaylistImportService service, Guid id) =>
        (await service.GetSessionsAsync()).Single(s => s.Id == id);

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

    /// <summary>Searches by title over a fixed catalog; can fail searches, and hold downloads until released.</summary>
    private sealed class ChoosePlugin : IMusicSourcePlugin
    {
        public PluginInfo Info { get; } = new("fake", "Fake", "", "#000000");

        public List<RemoteTrack> Catalog { get; } = [];

        public List<string> FailingSearchTitles { get; } = [];

        public Dictionary<string, int> DownloadDelays { get; } = [];

        /// <summary>When set, downloads wait for it before finishing.</summary>
        public TaskCompletionSource? Gate { get; set; }

        public int DownloadCount { get; private set; }

        public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
        {
            if (FailingSearchTitles.Any(query.Contains))
                throw new InvalidOperationException("Fuente no disponible");

            return Task.FromResult<IReadOnlyList<RemoteTrack>>(Catalog.Where(t => query.Contains(t.Title, StringComparison.OrdinalIgnoreCase)).ToList());
        }

        public async Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            DownloadCount++;
            if (Gate is not null)
                await Gate.Task.WaitAsync(cancellationToken);
            await Task.Delay(DownloadDelays.GetValueOrDefault(track.RemoteId, 10), cancellationToken);
            var path = Path.Combine(destinationDirectory, $"{track.RemoteId}.m4a");
            await File.WriteAllTextAsync(path, "audio", cancellationToken);
            return path;
        }
    }
}
