using Wired.Core.Abstractions;

namespace Wired.Infrastructure.Media;

/// <summary>Covers stored as files in the app data folder.</summary>
public sealed class FileArtworkStore(IAppPaths paths, HttpClient http) : IArtworkStore
{
    private readonly string _directory = Path.Combine(paths.DataDirectory, "artwork");

    public string? Find(string key)
    {
        var path = PathFor(key);
        return File.Exists(path) ? path : null;
    }

    public async Task<string?> SaveFromUrlAsync(string key, string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var image = await http.GetByteArrayAsync(url, cancellationToken);
            return image.Length == 0 ? null : await SaveAsync(key, image, cancellationToken);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            return null; // Offline or gone: retried by the next backfill.
        }
    }

    public async Task<string> SaveAsync(string key, byte[] image, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(key);
        await File.WriteAllBytesAsync(path, image, cancellationToken);
        return path;
    }

    private string PathFor(string key)
    {
        var safe = string.Concat(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_directory, $"{safe}.jpg");
    }
}
