using Wired.Core.Plugins;

namespace Wired.Core.Playback;

public sealed class PlaybackSourceResolver(IPluginCatalog plugins) : IPlaybackSourceResolver
{
    public async Task<PlaybackSource> ResolveAsync(PlaybackItem item, CancellationToken cancellationToken = default)
    {
        if (item.LocalTrack is { } local)
            return new FilePlaybackSource(local.FilePath);

        var remote = item.RemoteTrack!;
        if (plugins.Get(remote.SourceId) is not IStreamableMusicSource streamable)
            throw new NotSupportedException("Esta fuente no permite escuchar sin descargar.");

        // Plugins do network I/O: never run them on the caller's (UI) thread. Android forbids it.
        var stream = await Task.Run(() => streamable.OpenStreamAsync(remote, cancellationToken), cancellationToken);
        return new StreamPlaybackSource(stream);
    }
}
