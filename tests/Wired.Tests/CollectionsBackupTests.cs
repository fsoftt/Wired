using Wired.Core.Collections;
using Wired.Core.Models;
using Wired.Core.Storage;
using Wired.Infrastructure.Collections;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class CollectionsBackupTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly LocalTrack _a;
    private readonly LocalTrack _b;

    public CollectionsBackupTests()
    {
        _a = Track("a");
        _b = Track("b");
    }

    [Fact]
    public async Task Playlists_and_likes_survive_a_reinstall()
    {
        var before = new Install(_paths, _a, _b);
        var playlist = await before.Playlists.CreateAsync("Para correr");
        playlist = await before.Playlists.AddTrackAsync(playlist, _b);
        await before.Playlists.AddTrackAsync(playlist, _a);
        await before.Likes.SetLikedAsync(_a.Id, true);
        await before.Likes.SetLikedAsync(_b.Id, true); // newest
        await before.Backup.ExportAllAsync();

        // Reinstall: app data is gone, the music folder is not. Recovered tracks get new ids.
        Directory.Delete(_paths.DataDirectory, recursive: true);
        var a2 = _a with { Id = Guid.NewGuid() };
        var b2 = _b with { Id = Guid.NewGuid() };
        var after = new Install(_paths, a2, b2);

        var result = await after.Backup.RestoreAsync();

        Assert.Equal(new CollectionsRestoreResult(1, 2), result);
        var restored = Assert.Single(await after.Playlists.GetAllAsync());
        Assert.Equal((playlist.Id, "Para correr"), (restored.Id, restored.Name));
        Assert.Equal([b2, a2], await after.Playlists.GetTracksAsync(restored));
        Assert.Equal([b2, a2], await after.Likes.GetLikedTracksAsync());
    }

    [Fact]
    public async Task Restoring_twice_adds_nothing()
    {
        var install = new Install(_paths, _a);
        await install.Playlists.CreateAsync("Lista");
        await install.Likes.SetLikedAsync(_a.Id, true);
        await install.Backup.ExportAllAsync();

        Assert.Equal(new CollectionsRestoreResult(0, 0), await install.Backup.RestoreAsync());
    }

    [Fact]
    public async Task The_newest_copy_of_a_playlist_wins()
    {
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(_paths.MusicDirectory, "Playlists"));
        WriteBackup($"Old [wired-{id}].m3u", M3uPlaylistFormat.Write(id, "Vieja", [_a]), DateTime.UtcNow.AddDays(-1));
        WriteBackup($"New [wired-{id}] (2).m3u", M3uPlaylistFormat.Write(id, "Nueva", [_a, _b]), DateTime.UtcNow);

        var install = new Install(_paths, _a, _b);
        await install.Backup.RestoreAsync();

        var playlist = Assert.Single(await install.Playlists.GetAllAsync());
        Assert.Equal("Nueva", playlist.Name);
        Assert.Equal(2, playlist.TrackIds.Count);
    }

    [Fact]
    public async Task Restoring_uses_only_the_newest_copy_of_likes_and_ignores_the_rest()
    {
        Directory.CreateDirectory(Path.Combine(_paths.MusicDirectory, "Playlists"));
        var key = M3uPlaylistFormat.LikesKey;
        var now = DateTime.UtcNow;
        WriteBackup("Me gusta [wired-likes].m3u", M3uPlaylistFormat.Write(key, "Me gusta", []), now.AddDays(-5));
        WriteBackup("Me gusta [wired-likes] (2).m3u", M3uPlaylistFormat.Write(key, "Me gusta", [_a]), now.AddDays(-2));
        WriteBackup("Me gusta [wired-likes] (3).m3u", M3uPlaylistFormat.Write(key, "Me gusta", [_a, _b]), now);
        WriteBackup("Me gusta [wired-likes] (4).m3u", M3uPlaylistFormat.Write(key, "Me gusta", [_b]), now);

        var install = new Install(_paths, _a, _b);
        var result = await install.Backup.RestoreAsync();

        // Same time: the copy with most entries wins; the older ones are ignored.
        Assert.Equal(2, result.Likes);
    }

    [Fact]
    public async Task A_fresh_install_does_not_add_a_copy_over_an_existing_mirror()
    {
        var before = new Install(_paths, _a);
        await before.Likes.SetLikedAsync(_a.Id, true);
        await before.Backup.ExportAllAsync();
        var files = Directory.GetFiles(Path.Combine(_paths.MusicDirectory, "Playlists"));

        var after = new Install(_paths, _a);
        await after.Backup.ExportAllAsync(); // Empty state, nothing changed on disk.
        await after.Backup.RestoreAsync();
        await after.Backup.ExportAllAsync(); // Restored: same content as the file.

        Assert.Equal(files, Directory.GetFiles(Path.Combine(_paths.MusicDirectory, "Playlists")));
    }

    [Fact]
    public async Task Deleted_playlists_do_not_come_back()
    {
        var install = new Install(_paths, _a);
        var playlist = await install.Playlists.CreateAsync("Temporal");
        await install.Backup.ExportAllAsync();

        await install.Playlists.DeleteAsync(playlist);
        await install.Backup.RemoveNowAsync(playlist);

        Directory.Delete(_paths.DataDirectory, recursive: true);
        var after = new Install(_paths, _a);
        await after.Backup.RestoreAsync();
        Assert.Empty(await after.Playlists.GetAllAsync());
    }

    [Fact]
    public async Task Tombstones_hide_playlists_whose_old_file_remains()
    {
        var id = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(_paths.MusicDirectory, "Playlists"));
        WriteBackup($"X [wired-{id}].m3u", M3uPlaylistFormat.Write(id, "X", [_a]), DateTime.UtcNow.AddDays(-1));
        WriteBackup($"X [wired-{id}] (2).m3u", M3uPlaylistFormat.WriteTombstone(id, "X"), DateTime.UtcNow);

        var install = new Install(_paths, _a);
        await install.Backup.RestoreAsync();

        Assert.Empty(await install.Playlists.GetAllAsync());
    }

    [Fact]
    public void M3u_files_are_standard_and_round_trip()
    {
        var text = M3uPlaylistFormat.Write("key", "Mi lista", [_a]);

        Assert.StartsWith("#EXTM3U", text);
        Assert.Contains($"../{Path.GetFileName(_a.FilePath)}", text);
        Assert.True(M3uPlaylistFormat.TryRead(text, out var document));
        Assert.Equal(("key", "Mi lista", false), (document.Key, document.Name, document.IsDeleted));
        Assert.Equal([new TrackKey(_a.SourceId, _a.RemoteId)], document.Tracks);
    }

    public void Dispose() => _paths.Dispose();

    private LocalTrack Track(string name)
    {
        Directory.CreateDirectory(_paths.MusicDirectory);
        var path = Path.Combine(_paths.MusicDirectory, MusicFileNaming.FileNameFor(name, "Artist", "demo", name, ".m4a"));
        File.WriteAllText(path, "audio");
        return new LocalTrack(Guid.NewGuid(), name, "Artist", null, null, path, "demo", name, DateTimeOffset.Now);
    }

    private void WriteBackup(string fileName, string content, DateTime writtenAt)
    {
        var path = Path.Combine(_paths.MusicDirectory, "Playlists", fileName);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, writtenAt);
    }

    /// <summary>One installation of the app: its own data, sharing the music folder.</summary>
    private sealed class Install
    {
        public Install(TempPaths paths, params LocalTrack[] tracks)
        {
            var library = new InMemoryLibrary();
            library.Tracks.AddRange(tracks);
            var playlistRepository = new JsonPlaylistRepository(paths);
            var likedRepository = new JsonLikedTracksRepository(paths);

            Playlists = new PlaylistService(playlistRepository, library, new FixedClock(DateTimeOffset.Now));
            Likes = new LikesService(likedRepository, library);
            Backup = new CollectionsBackupService(playlistRepository, Playlists, likedRepository, Likes, library, paths);
        }

        public PlaylistService Playlists { get; }

        public LikesService Likes { get; }

        public CollectionsBackupService Backup { get; }
    }
}
