using Wired.Core.Models;

namespace Wired.Core.Abstractions;

/// <summary>Persistent copy of the Home tab sections, so they show right away on launch.</summary>
public interface IHomeFeedCache
{
    Task<HomeFeedEntry?> GetAsync(HomeSectionKind kind, CancellationToken cancellationToken = default);

    Task SetAsync(HomeSectionKind kind, HomeFeedEntry entry, CancellationToken cancellationToken = default);
}
