using Wired.Core.Abstractions;

namespace Wired.App.Services;

public sealed class MauiNetworkMonitor : INetworkMonitor
{
    public bool IsOnline => Connectivity.Current.NetworkAccess == NetworkAccess.Internet;

    public async Task WaitUntilOnlineAsync(CancellationToken cancellationToken = default)
    {
        if (IsOnline)
            return;

        var online = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, ConnectivityChangedEventArgs e)
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
                online.TrySetResult();
        }

        Connectivity.Current.ConnectivityChanged += OnChanged;
        try
        {
            // Re-check: the network may have come back before we subscribed.
            if (!IsOnline)
                await online.Task.WaitAsync(cancellationToken);
        }
        finally
        {
            Connectivity.Current.ConnectivityChanged -= OnChanged;
        }
    }
}
