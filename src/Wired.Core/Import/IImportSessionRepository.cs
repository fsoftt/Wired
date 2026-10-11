namespace Wired.Core.Import;

public interface IImportSessionRepository
{
    Task<IReadOnlyList<PlaylistImportSession>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or replaces the session with the same id.</summary>
    Task SaveAsync(PlaylistImportSession session, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
