using System.Collections.Concurrent;
using Wired.Core.Playback;

namespace Wired.Core.Lyrics;

/// <summary>
/// Finds the lyrics of a queue item: memory cache → disk cache → provider.
/// </summary>
public sealed class LyricsService(ILyricsProvider provider, ILyricsCache cache)
{
    /// <summary>Null values remember "not found" for this session only (lyrics may be added later).</summary>
    private readonly ConcurrentDictionary<string, Lyrics?> _memory = new();

    public async Task<Lyrics?> GetAsync(PlaybackItem item, CancellationToken cancellationToken = default)
    {
        var key = KeyFor(item);
        if (_memory.TryGetValue(key, out var remembered))
            return remembered;

        var lyrics = await cache.GetAsync(key, cancellationToken);
        if (lyrics is null)
        {
            var (artist, title) = TrackTitleCleaner.Clean(item.Title, item.Artist);
            var query = new LyricsQuery(title, artist, item.Duration);

            // Network I/O: never on the caller's (UI) thread.
            lyrics = await Task.Run(() => provider.FindAsync(query, cancellationToken), cancellationToken);
            if (lyrics is not null)
                await cache.SetAsync(key, lyrics, cancellationToken);
        }

        _memory[key] = lyrics;
        return lyrics;
    }

    /// <summary>Same key for a track whether it is streamed or downloaded.</summary>
    public static string KeyFor(PlaybackItem item)
    {
        var (source, remoteId) = item.LocalTrack is { } local
            ? (local.SourceId, local.RemoteId)
            : (item.RemoteTrack!.SourceId, item.RemoteTrack.RemoteId);

        return $"{source}_{remoteId}";
    }
}
