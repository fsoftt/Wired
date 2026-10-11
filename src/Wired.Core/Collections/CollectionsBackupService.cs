using System.Text;
using Wired.Core.Abstractions;
using Wired.Core.Models;
using Wired.Core.Storage;

namespace Wired.Core.Collections;

public sealed record CollectionsRestoreResult(int Playlists, int Likes);

/// <summary>
/// Mirrors playlists and likes as M3U files next to the music (Music/Wired/Playlists) so they
/// survive uninstalling the app, and restores them from there.
/// </summary>
/// <remarks>
/// After a reinstall Android treats files written by the previous installation as foreign: they can
/// be read (with permission) but not overwritten or deleted. Writes therefore fall back to
/// "Name [wired-key] (2).m3u". Those copies cannot be removed by the new installation, so few are
/// created: nothing is written when the newest copy already has the same content, and the empty state
/// of a fresh installation never replaces a mirror that has entries. Restoring takes the newest file
/// of each playlist (ties: most entries) and ignores the other copies; deleting writes a tombstone
/// when an old file cannot be removed.
/// </remarks>
public sealed class CollectionsBackupService(
    IPlaylistRepository playlistRepository,
    PlaylistService playlists,
    ILikedTracksRepository likedRepository,
    LikesService likes,
    ITrackLibrary library,
    IAppPaths paths)
{
    private const int MaxFileVariants = 100;
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string BackupDirectory => Path.Combine(paths.MusicDirectory, "Playlists");

    /// <summary>Keeps the files up to date from now on, and writes the current state once.</summary>
    public void Start()
    {
        playlists.PlaylistsChanged += (_, _) => Schedule(ExportPlaylistsAsync);
        playlists.PlaylistDeleted += (_, playlist) => Schedule(() => RemoveAsync(playlist));
        likes.LikeChanged += (_, _) => Schedule(ExportLikesAsync);

        // A fresh installation is still empty here: it must not bury the mirrors it is about to restore.
        Schedule(() => ExportPlaylistsAsync(startup: true));
        Schedule(() => ExportLikesAsync(startup: true));
    }

    /// <summary>Writes every playlist and the likes now.</summary>
    public async Task ExportAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await ExportPlaylistsAsync();
            await ExportLikesAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Removes the files of a deleted playlist now.</summary>
    public async Task RemoveNowAsync(Playlist playlist)
    {
        await _gate.WaitAsync();
        try
        {
            await RemoveAsync(playlist);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Adds the playlists missing from the app (by id) and likes every track liked in any backup.
    /// Run after the tracks themselves were recovered.
    /// </summary>
    public async Task<CollectionsRestoreResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(BackupDirectory))
            return new CollectionsRestoreResult(0, 0);

        var documents = NewestPerKey(ReadFiles());
        var tracks = (await library.GetAllStoredAsync(cancellationToken))
            .GroupBy(t => new TrackKey(t.SourceId, t.RemoteId))
            .ToDictionary(g => g.Key, g => g.First().Id);

        var existing = (await playlistRepository.GetAllAsync(cancellationToken)).Select(p => p.Id).ToHashSet();
        var restoredPlaylists = 0;
        foreach (var (_, document, writtenAt) in documents.Where(d => d.Document.Key != M3uPlaylistFormat.LikesKey))
        {
            if (document.IsDeleted || !Guid.TryParse(document.Key, out var id) || existing.Contains(id))
                continue;

            var trackIds = document.Tracks.Where(tracks.ContainsKey).Select(k => tracks[k]).Distinct().ToList();
            await playlists.ImportAsync(new Playlist(id, document.Name, trackIds, writtenAt), cancellationToken);
            restoredPlaylists++;
        }

        await likes.InitializeAsync(cancellationToken);
        var likedIds = documents
            .Where(d => d.Document.Key == M3uPlaylistFormat.LikesKey)
            .SelectMany(d => d.Document.Tracks)
            .Where(tracks.ContainsKey)
            .Select(k => tracks[k])
            .Distinct()
            .Where(id => !likes.IsLiked(id))
            .ToList();

        // Liking inserts at the top: go oldest first to keep "most recent first".
        for (var i = likedIds.Count - 1; i >= 0; i--)
            await likes.SetLikedAsync(likedIds[i], liked: true, cancellationToken);

        return new CollectionsRestoreResult(restoredPlaylists, likedIds.Count);
    }

    private Task ExportPlaylistsAsync() => ExportPlaylistsAsync(startup: false);

    private Task ExportLikesAsync() => ExportLikesAsync(startup: false);

    private async Task ExportPlaylistsAsync(bool startup)
    {
        var tracks = await TracksByIdAsync();
        foreach (var playlist in await playlistRepository.GetAllAsync())
        {
            var content = M3uPlaylistFormat.Write(KeyOf(playlist), playlist.Name, Resolve(playlist.TrackIds, tracks));
            Write(KeyOf(playlist), playlist.Name, content, keepNonEmpty: startup && playlist.TrackIds.Count == 0);
        }
    }

    private async Task ExportLikesAsync(bool startup)
    {
        var tracks = await TracksByIdAsync();
        var liked = Resolve(await likedRepository.GetAllAsync(), tracks);
        Write(M3uPlaylistFormat.LikesKey, "Me gusta", M3uPlaylistFormat.Write(M3uPlaylistFormat.LikesKey, "Me gusta", liked), keepNonEmpty: startup && liked.Count == 0);
    }

    private Task RemoveAsync(Playlist playlist)
    {
        var key = KeyOf(playlist);
        var undeletable = FilesOf(key).Where(file => !TryDelete(file)).ToList();

        // A file from a previous installation would bring the playlist back when restoring.
        if (undeletable.Count > 0)
            Write(key, playlist.Name, M3uPlaylistFormat.WriteTombstone(key, playlist.Name));

        return Task.CompletedTask;
    }
    /// <summary>
    /// Writes the first variant we are allowed to write and removes our other copies. Nothing is written
    /// when the newest copy already has this content, so a reinstall does not add a copy per launch.
    /// </summary>
    /// <param name="keepNonEmpty">Skip an empty state when the mirror already exists (startup, before restoring).</param>
    private void Write(string key, string name, string content, bool keepNonEmpty = false)
    {
        Directory.CreateDirectory(BackupDirectory);

        var newest = NewestPerKey(ReadFiles()).FirstOrDefault(f => f.Document.Key == key);
        if (newest is not null && (keepNonEmpty || string.Equals(SafeRead(newest.Path), content, StringComparison.Ordinal)))
        {
            foreach (var other in FilesOf(key).Where(f => !string.Equals(f, newest.Path, StringComparison.Ordinal)))
                TryDelete(other);
            return;
        }

        var baseName = $"{MusicFileNaming.Sanitize(name)} [wired-{key}]";

        for (var variant = 1; variant <= MaxFileVariants; variant++)
        {
            var file = Path.Combine(BackupDirectory, variant == 1 ? $"{baseName}{M3uPlaylistFormat.Extension}" : $"{baseName} ({variant}){M3uPlaylistFormat.Extension}");
            try
            {
                File.WriteAllText(file, content, Utf8);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue; // Belongs to a previous installation.
            }

            foreach (var other in FilesOf(key).Where(f => !string.Equals(f, file, StringComparison.Ordinal)))
                TryDelete(other);
            return;
        }
    }

    private IEnumerable<string> FilesOf(string key) =>
        Directory.Exists(BackupDirectory)
            ? Directory.EnumerateFiles(BackupDirectory, $"*{M3uPlaylistFormat.Extension}")
                .Where(f => Path.GetFileName(f).Contains($"[wired-{key}]", StringComparison.Ordinal))
                .ToList()
            : [];

    private sealed record BackupFile(string Path, PlaylistDocument Document, DateTimeOffset WrittenAt);

    /// <summary>One file per playlist id: the newest, or the one with most entries when they tie.</summary>
    private static List<BackupFile> NewestPerKey(IEnumerable<BackupFile> files) =>
        files.GroupBy(f => f.Document.Key)
            .Select(g => g.OrderByDescending(f => f.WrittenAt).ThenByDescending(f => f.Document.Tracks.Count).First())
            .ToList();

    private List<BackupFile> ReadFiles()
    {
        var files = new List<BackupFile>();
        if (!Directory.Exists(BackupDirectory))
            return files;

        foreach (var file in Directory.EnumerateFiles(BackupDirectory, $"*{M3uPlaylistFormat.Extension}"))
        {
            var text = SafeRead(file);
            if (text is not null && M3uPlaylistFormat.TryRead(text, out var document))
                files.Add(new BackupFile(file, document, File.GetLastWriteTimeUtc(file)));
        }

        return files;
    }

    private static string? SafeRead(string file)
    {
        try
        {
            return File.ReadAllText(file, Utf8);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null; // Unreadable without permission: skip.
        }
    }

    private async Task<Dictionary<Guid, LocalTrack>> TracksByIdAsync() =>
        (await library.GetAllStoredAsync()).ToDictionary(t => t.Id);

    private static List<LocalTrack> Resolve(IEnumerable<Guid> ids, Dictionary<Guid, LocalTrack> tracks) =>
        ids.Select(id => tracks.GetValueOrDefault(id)).OfType<LocalTrack>().ToList();

    private static string KeyOf(Playlist playlist) => playlist.Id.ToString("N");

    private static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return !File.Exists(file);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>File work is serialized and never runs on the caller's (UI) thread.</summary>
    private void Schedule(Func<Task> work) =>
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync();
            try
            {
                await work();
            }
            catch (Exception)
            {
                // Best effort: the app's own data is the source of truth; the mirror is retried on the next change.
            }
            finally
            {
                _gate.Release();
            }
        });
}
