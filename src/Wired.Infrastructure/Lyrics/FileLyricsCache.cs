using System.Text.Json;
using Wired.Core.Abstractions;
using Wired.Core.Lyrics;
using Wired.Infrastructure.Storage;

namespace Wired.Infrastructure.Lyrics;

/// <summary>One small JSON file per track in the app data directory.</summary>
public sealed class FileLyricsCache(IAppPaths paths) : ILyricsCache
{
    private readonly string _directory = Path.Combine(paths.DataDirectory, "lyrics");

    public async Task<Core.Lyrics.Lyrics?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, StorageJsonContext.Default.Lyrics, cancellationToken);
        }
        catch (JsonException)
        {
            return null; // Corrupted entry: fetch again.
        }
    }

    public async Task SetAsync(string key, Core.Lyrics.Lyrics lyrics, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        await using var stream = File.Create(PathFor(key));
        await JsonSerializer.SerializeAsync(stream, lyrics, StorageJsonContext.Default.Lyrics, cancellationToken);
    }

    private string PathFor(string key)
    {
        var safe = string.Concat(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_directory, $"{safe}.json");
    }
}
