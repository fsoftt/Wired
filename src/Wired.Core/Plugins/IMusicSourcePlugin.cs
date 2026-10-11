using Wired.Core.Models;

namespace Wired.Core.Plugins;

/// <summary>
/// Contract every music source must implement.
/// Adding a new source (SoundCloud, Bandcamp, a local server...) only requires
/// a new implementation of this interface registered in the DI container.
/// The app always calls plugins from background threads, so they may do blocking network I/O.
/// </summary>
public interface IMusicSourcePlugin
{
    PluginInfo Info { get; }

    Task<IReadOnlyList<RemoteTrack>> SearchAsync(
        string query,
        int maxResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Optional: the track behind a link shared from the source's app or website
    /// (e.g. "https://example.com/song/…"), or null when the link is not this source's.
    /// </summary>
    Task<RemoteTrack?> ResolveLinkAsync(Uri link, CancellationToken cancellationToken = default) =>
        Task.FromResult<RemoteTrack?>(null);

    /// <summary>
    /// Optional: cover of a track known only by its id (e.g. music recovered from the folder),
    /// built without any request. Null when the source cannot tell.
    /// </summary>
    string? ThumbnailUrlFor(string remoteId) => null;

    /// <summary>
    /// Downloads the audio of <paramref name="track"/> into <paramref name="destinationDirectory"/>.
    /// </summary>
    /// <returns>Full path of the downloaded audio file.</returns>
    Task<string> DownloadAsync(
        RemoteTrack track,
        string destinationDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
