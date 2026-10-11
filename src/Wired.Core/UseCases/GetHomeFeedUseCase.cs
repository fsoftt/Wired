using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.Throttling;

namespace Wired.Core.UseCases;

/// <summary>
/// Content of the Home tab. Its three sections are independent: each one is cached, requested and can fail on its own.
/// Sources are only asked again when the cached copy is older than its TTL, or when the user forces it.
/// </summary>
public sealed class GetHomeFeedUseCase(
    IPluginCatalog plugins,
    ITrackLibrary library,
    ILikedTracksRepository likes,
    IHomeFeedCache cache,
    IClock clock)
{
    public const int TrendingMaxResults = 25;

    public const int ForYouMaxResults = 25;

    /// <summary>How many of the user's songs the recommendations start from.</summary>
    public const int MaxSeeds = 3;

    /// <summary>ISO 3166-1 alpha-2 code of the country behind <see cref="HomeSectionKind.TrendingColombia"/>.</summary>
    public const string ColombiaRegion = "CO";

    public static readonly TimeSpan TrendingTimeToLive = TimeSpan.FromHours(6);

    public static readonly TimeSpan ForYouTimeToLive = TimeSpan.FromHours(12);

    /// <summary>Display names of the installed sources that can suggest music. Empty when there are none (the Home then has nothing to show).</summary>
    public IReadOnlyList<string> DiscoverableSourceNames =>
        DiscoverableSources().Select(source => source.Info.DisplayName).ToList();

    /// <summary>The last fetched copy of a section, however old, without any request. Null if there is none.</summary>
    public async Task<HomeSection?> GetCachedAsync(HomeSectionKind kind, CancellationToken cancellationToken = default)
    {
        var entry = await TryGetCachedAsync(kind, cancellationToken);
        return entry is null ? null : await ToSectionAsync(kind, entry, HomeFeedIssue.None, cancellationToken);
    }

    /// <summary>
    /// The section from the cache while it is fresh, else fetched again. When the fetch fails and there is a cached
    /// copy, that copy is returned with the <see cref="HomeSection.Issue"/> instead of an error.
    /// </summary>
    /// <param name="force">Fetch even if the cached copy is still fresh (pull to refresh).</param>
    /// <exception cref="Exception">The fetch failed and there is nothing cached.</exception>
    public async Task<HomeSection> LoadAsync(HomeSectionKind kind, bool force = false, CancellationToken cancellationToken = default)
    {
        var cached = await TryGetCachedAsync(kind, cancellationToken);
        if (cached is not null && !force && IsFresh(kind, cached))
            return await ToSectionAsync(kind, cached, HomeFeedIssue.None, cancellationToken);

        try
        {
            var tracks = kind switch
            {
                HomeSectionKind.Trending => await GetTrendingAsync(region: null, cancellationToken),
                HomeSectionKind.TrendingColombia => await GetTrendingAsync(ColombiaRegion, cancellationToken),
                _ => await GetForYouAsync(cancellationToken),
            };

            var entry = new HomeFeedEntry(clock.Now, tracks.ToList());
            // Nothing found (e.g. empty library) is not worth remembering: it would hide songs added later.
            if (tracks.Count > 0)
                await TrySaveAsync(kind, entry, cancellationToken);

            return new HomeSection(tracks, entry.FetchedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error) when (cached is not null)
        {
            var issue = error is SourceRateLimitedException ? HomeFeedIssue.RateLimited : HomeFeedIssue.Failed;
            return await ToSectionAsync(kind, cached, issue, cancellationToken);
        }
    }

    /// <summary>A copy from the future (the clock went backwards) cannot be trusted: it counts as old.</summary>
    private bool IsFresh(HomeSectionKind kind, HomeFeedEntry entry)
    {
        var age = clock.Now - entry.FetchedAt;
        return age >= TimeSpan.Zero && age < TimeToLive(kind);
    }

    /// <summary>The cache is an optimization: if it cannot be read, the section is simply fetched.</summary>
    private async Task<HomeFeedEntry?> TryGetCachedAsync(HomeSectionKind kind, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetAsync(kind, cancellationToken) is { Tracks: not null } entry ? entry : null;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Failing to remember the tracks must not lose the ones just fetched.</summary>
    private async Task TrySaveAsync(HomeSectionKind kind, HomeFeedEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await cache.SetAsync(kind, entry, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Shown now, fetched again next time.
        }
    }

    private static TimeSpan TimeToLive(HomeSectionKind kind) =>
        kind == HomeSectionKind.ForYou ? ForYouTimeToLive : TrendingTimeToLive;

    /// <summary>Cached recommendations may include songs the user downloaded since: leave them out.</summary>
    private async Task<HomeSection> ToSectionAsync(HomeSectionKind kind, HomeFeedEntry entry, HomeFeedIssue issue, CancellationToken cancellationToken)
    {
        IReadOnlyList<RemoteTrack> tracks = entry.Tracks;
        if (kind == HomeSectionKind.ForYou)
        {
            var owned = await library.GetAllAsync(cancellationToken);
            tracks = entry.Tracks.Where(t => !owned.Any(local => local.IsSameAs(t))).ToList();
        }

        return new HomeSection(tracks, entry.FetchedAt, issue);
    }

    /// <summary>
    /// What is popular right now (globally, or in <paramref name="region"/>) on the sources that know it.
    /// The user is waiting for it: interactive priority.
    /// </summary>
    /// <exception cref="Exception">Every discoverable source failed; the error of the first one.</exception>
    private async Task<IReadOnlyList<RemoteTrack>> GetTrendingAsync(string? region, CancellationToken cancellationToken)
    {
        var sources = DiscoverableSources();
        var results = await Task.WhenAll(sources.Select(source => TryAsync(
            () => Task.Run(() => source.GetTrendingAsync(region, TrendingMaxResults, cancellationToken), cancellationToken), cancellationToken)));

        ThrowIfAllFailed(results);

        return results
            .Select(r => r.Tracks)
            .Interleave()
            .DistinctBy(t => (t.SourceId, t.RemoteId))
            .ToList();
    }

    /// <summary>
    /// Songs similar to the ones the user likes (first) and has added lately, leaving out those they already own.
    /// Empty when the library has no song of a discoverable source. Not urgent: background priority, so the
    /// requests are paced and back off with the rest of the background work and never delay what the user taps.
    /// </summary>
    /// <exception cref="Exception">Every request failed; the error of the first one.</exception>
    private async Task<IReadOnlyList<RemoteTrack>> GetForYouAsync(CancellationToken cancellationToken)
    {
        var owned = await library.GetAllAsync(cancellationToken);
        var seeds = await PickSeedsAsync(owned, cancellationToken);
        if (seeds.Count == 0)
            return [];

        (IReadOnlyList<RemoteTrack> Tracks, Exception? Error)[] results;
        using (RequestPriority.Background())
        {
            results = await Task.WhenAll(seeds.Select(seed => TryAsync(() => Task.Run(
                () => ((IDiscoverableMusicSource)plugins.Get(seed.SourceId)).GetRelatedAsync(seed.RemoteId, ForYouMaxResults, cancellationToken),
                cancellationToken), cancellationToken)));
        }

        ThrowIfAllFailed(results);

        return results
            .Select(r => r.Tracks)
            .Interleave()
            .Where(t => !seeds.Any(seed => seed.IsSameAs(t)) && !owned.Any(local => local.IsSameAs(t)))
            .DistinctBy(t => (t.SourceId, t.RemoteId))
            .Take(ForYouMaxResults)
            .ToList();
    }

    private List<IDiscoverableMusicSource> DiscoverableSources() =>
        plugins.Plugins.OfType<IDiscoverableMusicSource>().ToList();

    /// <summary>Liked songs first (latest like first), then the most recently added ones.</summary>
    private async Task<List<LocalTrack>> PickSeedsAsync(IReadOnlyList<LocalTrack> owned, CancellationToken cancellationToken)
    {
        var discoverable = DiscoverableSources().Select(s => s.Info.Id).ToHashSet();
        var candidates = owned.Where(t => discoverable.Contains(t.SourceId)).ToList();

        var tracksById = candidates.ToDictionary(t => t.Id);
        var liked = (await likes.GetAllAsync(cancellationToken))
            .Select(id => tracksById.GetValueOrDefault(id))
            .OfType<LocalTrack>();

        return liked
            .Concat(candidates.OrderByDescending(t => t.AddedAt))
            .DistinctBy(t => (t.SourceId, t.RemoteId))
            .Take(MaxSeeds)
            .ToList();
    }

    private static async Task<(IReadOnlyList<RemoteTrack> Tracks, Exception? Error)> TryAsync(Func<Task<IReadOnlyList<RemoteTrack>>> call, CancellationToken cancellationToken)
    {
        try
        {
            return (await call(), null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            return ([], error);
        }
    }

    private static void ThrowIfAllFailed(IReadOnlyList<(IReadOnlyList<RemoteTrack> Tracks, Exception? Error)> results)
    {
        if (results.Count > 0 && results.All(r => r.Error is not null))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(results[0].Error!).Throw();
    }
}

internal static class InterleaveExtensions
{
    /// <summary>Takes one item from each list in turn, so no list dominates the start.</summary>
    public static IEnumerable<T> Interleave<T>(this IEnumerable<IReadOnlyList<T>> lists)
    {
        var all = lists.ToList();
        for (var index = 0; all.Any(l => index < l.Count); index++)
        {
            foreach (var list in all.Where(l => index < l.Count))
                yield return list[index];
        }
    }
}
