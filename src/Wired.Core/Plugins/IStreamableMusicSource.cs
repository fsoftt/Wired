using Wired.Core.Models;

namespace Wired.Core.Plugins;

/// <summary>
/// Optional capability: a source whose tracks can be played directly from the network,
/// without downloading them to the device first.
/// </summary>
public interface IStreamableMusicSource : IMusicSourcePlugin
{
    /// <summary>
    /// Opens the audio of <paramref name="track"/> as a seekable stream. The source's own HTTP client
    /// does the requests, so streaming works wherever downloading works (signed URLs, throttling...).
    /// The caller owns the returned stream.
    /// </summary>
    Task<Stream> OpenStreamAsync(RemoteTrack track, CancellationToken cancellationToken = default);
}
