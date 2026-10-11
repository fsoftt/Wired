namespace Wired.Core.Abstractions;

/// <summary>Internet connectivity as reported by the platform.</summary>
public interface INetworkMonitor
{
    bool IsOnline { get; }

    /// <summary>Completes when the device is (or comes back) online.</summary>
    Task WaitUntilOnlineAsync(CancellationToken cancellationToken = default);
}

/// <summary>For hosts without connectivity information (and tests): always online.</summary>
public sealed class AlwaysOnlineNetworkMonitor : INetworkMonitor
{
    public bool IsOnline => true;

    public Task WaitUntilOnlineAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
