using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Core.Models;

namespace Wired.Core.UseCases;

/// <summary>
/// Removes a track from the library, its playlists and likes, and deletes its audio file.
/// </summary>
public sealed class RemoveTrackUseCase(ITrackLibrary library, PlaylistService playlists, LikesService likes)
{
    public async Task ExecuteAsync(LocalTrack track, CancellationToken cancellationToken = default)
    {
        await library.RemoveAsync(track.Id, cancellationToken);
        await playlists.RemoveTrackFromAllAsync(track.Id, cancellationToken);
        await likes.SetLikedAsync(track.Id, liked: false, cancellationToken);

        try
        {
            if (File.Exists(track.FilePath))
                File.Delete(track.FilePath);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Files kept from a previous installation may belong to it as far as the OS is concerned.
            // The track leaves the library anyway; the file stays in the music folder.
        }
    }
}
