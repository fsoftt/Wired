using Wired.Core.Models;
using Wired.Core.Plugins;

namespace Wired.Core.Throttling;

/// <summary>
/// Decorator that sends every call of a plugin through its <see cref="RequestThrottle"/>.
/// Background calls that hit a rate limit wait for the cooldown and retry by themselves.
/// </summary>
public class ThrottledMusicSource(IMusicSourcePlugin inner, RequestThrottle throttle) : IMusicSourcePlugin
{
    public PluginInfo Info => inner.Info;

    public RequestThrottle Throttle { get; } = throttle;

    /// <summary>Wraps <paramref name="plugin"/>, keeping its optional capabilities.</summary>
    public static IMusicSourcePlugin Wrap(IMusicSourcePlugin plugin, RequestThrottle throttle) =>
        plugin switch
        {
            IStreamableMusicSource streamable and IDiscoverableMusicSource discoverable => new ThrottledFullMusicSource(streamable, discoverable, throttle),
            IStreamableMusicSource streamable => new ThrottledStreamableMusicSource(streamable, throttle),
            IDiscoverableMusicSource discoverable => new ThrottledDiscoverableMusicSource(discoverable, throttle),
            _ => new ThrottledMusicSource(plugin, throttle),
        };

    public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.SearchAsync(query, maxResults, cancellationToken), cancellationToken);

    public string? ThumbnailUrlFor(string remoteId) => inner.ThumbnailUrlFor(remoteId);

    public Task<RemoteTrack?> ResolveLinkAsync(Uri link, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.ResolveLinkAsync(link, cancellationToken), cancellationToken);

    /// <summary>Only the start is paced: the transfer itself is bandwidth, not request rate.</summary>
    public Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.DownloadAsync(track, destinationDirectory, progress, cancellationToken), cancellationToken);

    protected async Task<T> ExecuteAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await Throttle.WaitTurnAsync(cancellationToken);
            try
            {
                var result = await call();
                Throttle.ReportSuccess();
                return result;
            }
            catch (SourceRateLimitedException limited)
            {
                Throttle.ReportRateLimited(limited.RetryAfter);

                // The user is waiting for an answer: tell them now. Background work waits it out.
                if (!RequestPriority.IsBackground || attempt >= Throttle.Options.MaxBackgroundAttempts)
                    throw;
            }
        }
    }
}

public sealed class ThrottledStreamableMusicSource(IStreamableMusicSource inner, RequestThrottle throttle)
    : ThrottledMusicSource(inner, throttle), IStreamableMusicSource
{
    public Task<Stream> OpenStreamAsync(RemoteTrack track, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.OpenStreamAsync(track, cancellationToken), cancellationToken);
}

public sealed class ThrottledDiscoverableMusicSource(IDiscoverableMusicSource inner, RequestThrottle throttle)
    : ThrottledMusicSource(inner, throttle), IDiscoverableMusicSource
{
    public Task<IReadOnlyList<RemoteTrack>> GetTrendingAsync(string? region, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.GetTrendingAsync(region, maxResults, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<RemoteTrack>> GetRelatedAsync(string remoteId, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => inner.GetRelatedAsync(remoteId, maxResults, cancellationToken), cancellationToken);
}

/// <summary>A source that is both streamable and discoverable.</summary>
public sealed class ThrottledFullMusicSource(IStreamableMusicSource streamable, IDiscoverableMusicSource discoverable, RequestThrottle throttle)
    : ThrottledMusicSource(streamable, throttle), IStreamableMusicSource, IDiscoverableMusicSource
{
    public Task<Stream> OpenStreamAsync(RemoteTrack track, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => streamable.OpenStreamAsync(track, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<RemoteTrack>> GetTrendingAsync(string? region, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => discoverable.GetTrendingAsync(region, maxResults, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<RemoteTrack>> GetRelatedAsync(string remoteId, int maxResults, CancellationToken cancellationToken = default) =>
        ExecuteAsync(() => discoverable.GetRelatedAsync(remoteId, maxResults, cancellationToken), cancellationToken);
}
