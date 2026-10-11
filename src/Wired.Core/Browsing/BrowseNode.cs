namespace Wired.Core.Browsing;

/// <summary>An entry in the car / external browser tree: a folder (browsable) or something to play.</summary>
public sealed record BrowseNode(string Id, string Title, string? Subtitle, string? Artwork, bool IsPlayable);
