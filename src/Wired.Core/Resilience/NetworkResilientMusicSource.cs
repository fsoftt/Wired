using Wired.Core.Abstractions;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.Throttling;

namespace Wired.Core.Resilience;

/// <summary>
/// Decorator that makes background calls survive connection drops: it waits until the device is
/// online again and retries with growing delays. Calls the user is waiting for fail right away.
/// </summary>
public class NetworkResilientMusicSource(IMusicSourcePlugin inner, INetworkMonitor network, ResilienceOptions options)
    : IMusicSourcePlugin
{
    public PluginInfo Info => inner.Info;

    public static IMusicSourcePlugin Wrap(IMusicSourcePlugin plugin, INetworkMonitor network, ResilienceOptions options) =>
        plugin switch
        {
            IStreamableMusicSource streamable and IDiscoverableMusicSource discoverable => new NetworkResilientFullMusicSource(streamable, discoverable, network, options),
            IStreamableMusicSource streamable => new NetworkResilientStreamableMusicSource(streamable, network, options),
            IDiscoverableMusicSource discoverable => new NetworkResilientDiscoverableMusicSource(discoverable, network, options),
            _ => new NetworkResilientMusicSource(plugin, network, options),
        };

    public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.SearchAsync(query, maxResults, cancellationToken), cancellationToken);

    public string? ThumbnailUrlFor(string remoteId) => inner.ThumbnailUrlFor(remoteId);

    public Task<RemoteTrack?> ResolveLinkAsync(Uri link, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.ResolveLinkAsync(link, cancellationToken), cancellationToken);

    public Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.DownloadAsync(track, destinationDirectory, progress, cancellationToken), cancellationToken);

    protected async Task<T> ExecuteAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        var delay = options.InitialRetryDelay;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (Exception error) when (RequestPriority.IsBackground
                                          && attempt < options.MaxBackgroundAttempts
                                          && TransientNetworkError.IsTransient(error, cancellationToken))
            {
                await network.WaitUntilOnlineAsync(cancellationToken);
                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, options.MaxRetryDelay.Ticks));
            }
        }
    }
}

public sealed class NetworkResilientStreamableMusicSource(IStreamableMusicSource inner, INetworkMonitor network, ResilienceOptions options)
    : NetworkResilientMusicSource(inner, network, options), IStreamableMusicSource
{
    public Task<Stream> OpenStreamAsync(RemoteTrack track, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.OpenStreamAsync(track, cancellationToken), cancellationToken);
}

public sealed class NetworkResilientDiscoverableMusicSource(IDiscoverableMusicSource inner, INetworkMonitor network, ResilienceOptions options)
    : NetworkResilientMusicSource(inner, network, options), IDiscoverableMusicSource
{
    public Task<IReadOnlyList<RemoteTrack>> GetTrendingAsync(string? region, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.GetTrendingAsync(region, maxResults, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<RemoteTrack>> GetRelatedAsync(string remoteId, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.GetRelatedAsync(remoteId, maxResults, cancellationToken), cancellationToken);
}

/// <summary>A source that is both streamable and discoverable.</summary>
public sealed class NetworkResilientFullMusicSource(IStreamableMusicSource streamable, IDiscoverableMusicSource discoverable, INetworkMonitor network, ResilienceOptions options)
    : NetworkResilientMusicSource(streamable, network, options), IStreamableMusicSource, IDiscoverableMusicSource
{
    public Task<Stream> OpenStreamAsync(RemoteTrack track, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => streamable.OpenStreamAsync(track, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<RemoteTrack>> GetTrendingAsync(string? region, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => discoverable.GetTrendingAsync(region, maxResults, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<RemoteTrack>> GetRelatedAsync(string remoteId, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => discoverable.GetRelatedAsync(remoteId, maxResults, cancellationToken), cancellationToken);
}
