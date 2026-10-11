namespace Wired.Core.Collections;

public interface IPlaylistRepository
{
    Task<IReadOnlyList<Playlist>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces the playlist with the same id.</summary>
    Task SaveAsync(Playlist playlist, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid playlistId, CancellationToken cancellationToken = default);
}
