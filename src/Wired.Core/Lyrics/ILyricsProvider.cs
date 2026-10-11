namespace Wired.Core.Lyrics;

public sealed record LyricsQuery(string Title, string Artist, TimeSpan? Duration);

/// <summary>
/// A lyrics database (LRCLIB, Musixmatch...). Called from background threads.
/// </summary>
public interface ILyricsProvider
{
    /// <returns>The lyrics, or null when the database has none for this track.</returns>
    Task<Lyrics?> FindAsync(LyricsQuery query, CancellationToken cancellationToken = default);
}
