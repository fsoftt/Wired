namespace Wired.Core.Lyrics;

/// <summary>Persistent lyrics storage so downloaded tracks show lyrics offline.</summary>
public interface ILyricsCache
{
    Task<Lyrics?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task SetAsync(string key, Lyrics lyrics, CancellationToken cancellationToken = default);
}
