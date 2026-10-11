using System.Collections.Concurrent;
using Wired.Core.Plugins;

namespace Wired.Core.Throttling;

/// <summary>One throttle per music source, shared by every caller.</summary>
public sealed class RequestThrottleRegistry(ThrottleOptions options, TimeProvider? time = null)
{
    private readonly ConcurrentDictionary<string, RequestThrottle> _throttles = new();

    /// <summary>Raised when any source starts or ends a cooldown.</summary>
    public event EventHandler<RequestThrottle>? StateChanged;

    public RequestThrottle For(string sourceId) =>
        _throttles.GetOrAdd(sourceId, id =>
        {
            var throttle = new RequestThrottle(id, options, time);
            throttle.StateChanged += (_, _) => StateChanged?.Invoke(this, throttle);
            return throttle;
        });

    public IEnumerable<IMusicSourcePlugin> Wrap(IEnumerable<IMusicSourcePlugin> plugins) =>
        plugins.Select(p => ThrottledMusicSource.Wrap(p, For(p.Info.Id)));
}
