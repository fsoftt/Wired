namespace Wired.Core.Models;

/// <summary>A section of the Home tab as it was last fetched, kept to show it without asking the source again.</summary>
public sealed record HomeFeedEntry(DateTimeOffset FetchedAt, List<RemoteTrack> Tracks);
