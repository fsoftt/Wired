using Wired.Core.Abstractions;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.Storage;

namespace Wired.Core.UseCases;

/// <summary>
/// Downloads a remote track with its plugin and adds it to the library.
/// Downloading a track that is already in the library is a no-op.
/// </summary>
public sealed class DownloadTrackUseCase(
    IPluginCatalog plugins,
    ITrackLibrary library,
    IAppPaths paths,
    IClock clock)
{
    public async Task<LocalTrack> ExecuteAsync(
        RemoteTrack track,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await library.FindAsync(track.SourceId, track.RemoteId, cancellationToken);
        if (existing is not null)
            return existing;

        var plugin = plugins.Get(track.SourceId);

        // "Artist - Song (Official Video)" by "ArtistVEVO" is stored as "Song" by "Artist".
        var (artist, title) = Lyrics.TrackTitleCleaner.Clean(track.Title, track.Artist);
        if (title.Length == 0)
            title = track.Title;
        if (artist.Length == 0)
            artist = track.Artist;

        // Download to scratch space first: a canceled or failed download never leaves
        // half-written files in the music folder.
        var staging = Path.Combine(paths.CacheDirectory, "downloads", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string filePath;
        try
        {
            var downloaded = await plugin.DownloadAsync(track, staging, progress, cancellationToken);

            var fileName = MusicFileNaming.FileNameFor(title, artist, track.SourceId, track.RemoteId, Path.GetExtension(downloaded));
            filePath = MoveIntoLibrary(downloaded, fileName);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }

        var localTrack = new LocalTrack(
            Id: Guid.NewGuid(),
            Title: title,
            Artist: artist,
            Duration: track.Duration,
            ThumbnailUrl: track.ThumbnailUrl,
            FilePath: filePath,
            SourceId: track.SourceId,
            RemoteId: track.RemoteId,
            AddedAt: clock.Now);

        var stored = await library.AddAsync(localTrack, cancellationToken);
        if (stored.Id != localTrack.Id && stored.FilePath != filePath)
            TryDeleteFile(filePath); // Someone else added it meanwhile: keep a single copy.
        return stored;
    }

    /// <summary>
    /// Moves the file to the shared music folder. If the platform refuses it, the download is kept in
    /// app storage instead of failing; <see cref="MoveMusicToSharedFolderUseCase"/> retries on next start.
    /// </summary>
    private string MoveIntoLibrary(string downloaded, string fileName)
    {
        try
        {
            Directory.CreateDirectory(paths.MusicDirectory);
            var shared = Path.Combine(paths.MusicDirectory, fileName);
            File.Move(downloaded, shared, overwrite: true);
            return shared;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            var fallbackDirectory = Path.Combine(paths.DataDirectory, "music");
            Directory.CreateDirectory(fallbackDirectory);
            var fallback = Path.Combine(fallbackDirectory, fileName);
            File.Move(downloaded, fallback, overwrite: true);
            return fallback;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Cache is cleaned by the OS eventually.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover file is skipped by the folder scan: it is already in the library.
        }
    }
}
