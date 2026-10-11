namespace Wired.Core.Collections;

public interface ILikedTracksRepository
{
    /// <summary>Ids of the liked tracks, most recently liked first.</summary>
    Task<IReadOnlyList<Guid>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SetLikedAsync(Guid trackId, bool liked, CancellationToken cancellationToken = default);
}
