namespace Wired.Core.Lyrics;

/// <summary>A line of synchronized lyrics and the moment it starts.</summary>
public sealed record LyricLine(TimeSpan Time, string Text);
