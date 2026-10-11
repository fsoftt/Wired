namespace Wired.Core.Models;

public enum HomeSectionKind
{
    Trending,
    TrendingColombia,
    ForYou,
}

/// <summary>Why a section shows older content than asked for.</summary>
public enum HomeFeedIssue
{
    None,
    RateLimited,
    Failed,
}

/// <param name="FetchedAt">When the tracks were fetched from the source (they may come from the cache).</param>
/// <param name="Issue">Set when a refresh failed and <paramref name="Tracks"/> is the cached copy.</param>
public sealed record HomeSection(IReadOnlyList<RemoteTrack> Tracks, DateTimeOffset? FetchedAt, HomeFeedIssue Issue = HomeFeedIssue.None);
