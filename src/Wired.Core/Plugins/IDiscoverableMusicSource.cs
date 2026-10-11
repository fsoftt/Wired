using Wired.Core.Models;

namespace Wired.Core.Plugins;

/// <summary>
/// Optional capability: a source that can suggest music without a search query, so the Home tab
/// has something to show.
/// </summary>
public interface IDiscoverableMusicSource : IMusicSourcePlugin
{
    /// <summary>
    /// What is popular on the source right now (its current chart), most popular first.
    /// <paramref name="region"/> is an ISO 3166-1 alpha-2 country code (e.g. "CO") for that country's chart, or null for the global one.
    /// A region the source has no chart for gives an empty list.
    /// </summary>
    Task<IReadOnlyList<RemoteTrack>> GetTrendingAsync(string? region, int maxResults, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tracks similar to the track <paramref name="remoteId"/> of this source, without that track itself.
    /// </summary>
    Task<IReadOnlyList<RemoteTrack>> GetRelatedAsync(string remoteId, int maxResults, CancellationToken cancellationToken = default);
}
