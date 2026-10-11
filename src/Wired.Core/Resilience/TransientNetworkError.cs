using System.Net.Sockets;

namespace Wired.Core.Resilience;

/// <summary>
/// Recognizes failures caused by the connection itself (no network, DNS, timeouts, resets),
/// which are worth retrying, as opposed to answers from the service (404, unavailable video...).
/// </summary>
public static class TransientNetworkError
{
    /// <summary>Java exceptions surfaced by Android's native HTTP stack (matched by name: Core has no Android types).</summary>
    private static readonly string[] JavaNetworkErrors =
    [
        "UnknownHostException", "SocketTimeoutException", "ConnectException", "SocketException",
        "NoRouteToHostException", "SSLException", "SSLHandshakeException", "InterruptedIOException",
    ];

    public static bool IsTransient(Exception error, CancellationToken cancellationToken = default)
    {
        for (var current = error; current is not null; current = current.InnerException!)
        {
            switch (current)
            {
                // A response with a status code means the network worked.
                case HttpRequestException { StatusCode: not null }:
                    return false;
                // Disk problems are not fixed by waiting for the network.
                case FileNotFoundException or DirectoryNotFoundException or PathTooLongException or DriveNotFoundException:
                    return false;
                case HttpRequestException:
                case SocketException:
                case TimeoutException:
                case IOException:
                    return true;
                case var other when JavaNetworkErrors.Contains(other.GetType().Name):
                    return true;
                // HttpClient timeouts surface as cancellations nobody asked for.
                case TaskCanceledException when !cancellationToken.IsCancellationRequested:
                    return true;
            }
        }

        return false;
    }
}
