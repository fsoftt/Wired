using Wired.Core.Models;

namespace Wired.Core.Abstractions;

/// <summary>
/// Persistent collection of downloaded tracks.
/// </summary>
public interface ITrackLibrary
{
    /// <summary>Tracks whose audio file is currently reachable.</summary>
    Task<IReadOnlyList<LocalTrack>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Every stored track, including those whose file is missing or not readable right now.</summary>
    Task<IReadOnlyList<LocalTrack>> GetAllStoredAsync(CancellationToken cancellationToken = default);

    Task<LocalTrack?> FindAsync(string sourceId, string remoteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the track unless one with the same source and remote id is already stored.
    /// </summary>
    /// <returns>The stored track: <paramref name="track"/> or the one that was already there.</returns>
    Task<LocalTrack> AddAsync(LocalTrack track, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps one track per source and remote id and forgets the others.
    /// </summary>
    /// <returns>Id of every forgotten track mapped to the id of the track kept in its place.</returns>
    Task<IReadOnlyDictionary<Guid, Guid>> RemoveDuplicatesAsync(CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid trackId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored track with the same id.</summary>
    Task UpdateAsync(LocalTrack track, CancellationToken cancellationToken = default);
}
