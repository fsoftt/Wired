using System.Text.Json;
using Wired.Core.Abstractions;
using Wired.Core.Models;
using Wired.Infrastructure.Storage;

namespace Wired.Infrastructure.Home;

/// <summary>One small JSON file per Home section in the app data directory.</summary>
public sealed class FileHomeFeedCache(IAppPaths paths) : IHomeFeedCache
{
    private readonly string _directory = Path.Combine(paths.DataDirectory, "home");

    public async Task<HomeFeedEntry?> GetAsync(HomeSectionKind kind, CancellationToken cancellationToken = default)
    {
        var path = PathFor(kind);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync(stream, StorageJsonContext.Default.HomeFeedEntry, cancellationToken) is { Tracks: not null } entry ? entry : null;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        {
            return null; // Corrupted entry: fetch again.
        }
    }

    public async Task SetAsync(HomeSectionKind kind, HomeFeedEntry entry, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);

        // Write to a temp file first so a crash never leaves a corrupted file.
        var path = PathFor(kind);
        var tempFile = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, entry, StorageJsonContext.Default.HomeFeedEntry, cancellationToken);
            }

            File.Move(tempFile, path, overwrite: true);
        }
        finally
        {
            File.Delete(tempFile); // Gone after a successful move; left over only if the write failed.
        }
    }

    private string PathFor(HomeSectionKind kind) => Path.Combine(_directory, $"{kind.ToString().ToLowerInvariant()}.json");
}
