namespace Wired.Core.Abstractions;

/// <summary>Covers kept on the device, one per track key ("source_remoteId").</summary>
public interface IArtworkStore
{
    /// <summary>Path of the stored cover, if any.</summary>
    string? Find(string key);

    /// <returns>Path of the stored file, or null when the image could not be fetched.</returns>
    Task<string?> SaveFromUrlAsync(string key, string url, CancellationToken cancellationToken = default);

    Task<string> SaveAsync(string key, byte[] image, CancellationToken cancellationToken = default);
}
