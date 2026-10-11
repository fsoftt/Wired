namespace Wired.Core.Throttling;

/// <summary>
/// Ambient priority of the requests made by the current async flow. Background work (imports,
/// downloads) is paced and paused when a source rate-limits us; what the user just tapped is not.
/// </summary>
/// <example><code>using (RequestPriority.Background()) { await plugin.SearchAsync(...); }</code></example>
public static class RequestPriority
{
    private static readonly AsyncLocal<bool> BackgroundFlag = new();

    public static bool IsBackground => BackgroundFlag.Value;

    public static IDisposable Background()
    {
        var previous = BackgroundFlag.Value;
        BackgroundFlag.Value = true;
        return new Restore(previous);
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose() => BackgroundFlag.Value = previous;
    }
}
