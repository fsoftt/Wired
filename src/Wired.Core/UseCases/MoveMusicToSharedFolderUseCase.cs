using Wired.Core.Abstractions;
using Wired.Core.Storage;

namespace Wired.Core.UseCases;

/// <summary>
/// One-off migration: moves tracks downloaded by older versions (inside the app's private storage,
/// which is wiped on uninstall) to the music folder, renaming them so they can be recovered.
/// </summary>
public sealed class MoveMusicToSharedFolderUseCase(ITrackLibrary library, IAppPaths paths)
{
    /// <returns>Number of tracks moved.</returns>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var musicDirectory = Path.GetFullPath(paths.MusicDirectory);
        var moved = 0;

        foreach (var track in await library.GetAllStoredAsync(cancellationToken))
        {
            if (!File.Exists(track.FilePath) || Path.GetFullPath(Path.GetDirectoryName(track.FilePath)!) == musicDirectory)
                continue;

            Directory.CreateDirectory(musicDirectory);
            var destination = Path.Combine(musicDirectory, MusicFileNaming.FileNameFor(track));

            try
            {
                File.Move(track.FilePath, destination, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue; // Keep it where it is; it still plays.
            }

            await library.UpdateAsync(track with { FilePath = destination }, cancellationToken);
            moved++;
        }

        return moved;
    }
}
