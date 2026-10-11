using System.Net;
using System.Net.Sockets;
using Wired.Core.Abstractions;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.Resilience;
using Wired.Core.Throttling;

namespace Wired.Tests;

public class ResilienceTests
{
    private static readonly ResilienceOptions Fast = new()
    {
        InitialRetryDelay = TimeSpan.FromMilliseconds(10),
        MaxRetryDelay = TimeSpan.FromMilliseconds(40),
        MaxBackgroundAttempts = 4,
    };

    public static TheoryData<Exception, bool> Errors => new()
    {
        { new HttpRequestException("Connection failure", new IOException("Unable to resolve host")), true },
        { new HttpRequestException("x", new UnknownHostException()), true },
        { new SocketException(), true },
        { new TaskCanceledException("HttpClient timeout"), true },
        { new HttpRequestException("Not found", null, HttpStatusCode.NotFound), false },
        { new DirectoryNotFoundException(), false },
        { new InvalidOperationException("video unavailable"), false },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void Classifies_network_failures(Exception error, bool transient) =>
        Assert.Equal(transient, TransientNetworkError.IsTransient(error));

    [Fact]
    public async Task Background_calls_wait_for_the_network_and_retry()
    {
        var network = new ToggleNetwork(online: false);
        var plugin = new DroppingPlugin(failures: 2);
        var source = NetworkResilientMusicSource.Wrap(plugin, network, Fast);

        Task<IReadOnlyList<RemoteTrack>> search;
        using (RequestPriority.Background())
            search = source.SearchAsync("q", 5);

        await Task.Delay(100);
        Assert.False(search.IsCompleted); // Waiting for connectivity.

        network.GoOnline();
        Assert.Single(await search);
        Assert.Equal(3, plugin.Calls);
    }

    [Fact]
    public async Task Interactive_calls_fail_right_away()
    {
        var source = NetworkResilientMusicSource.Wrap(new DroppingPlugin(failures: 1), new ToggleNetwork(true), Fast);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.SearchAsync("q", 5));
    }

    [Fact]
    public async Task Gives_up_after_the_maximum_attempts()
    {
        var plugin = new DroppingPlugin(failures: 100);
        var source = NetworkResilientMusicSource.Wrap(plugin, new ToggleNetwork(true), Fast);

        using (RequestPriority.Background())
            await Assert.ThrowsAsync<HttpRequestException>(() => source.SearchAsync("q", 5));

        Assert.Equal(Fast.MaxBackgroundAttempts, plugin.Calls);
    }

    /// <summary>Same simple name as Android's java.net.UnknownHostException.</summary>
    private sealed class UnknownHostException : Exception;

    private sealed class ToggleNetwork(bool online) : INetworkMonitor
    {
        private readonly TaskCompletionSource _online = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsOnline { get; private set; } = online;

        public void GoOnline()
        {
            IsOnline = true;
            _online.TrySetResult();
        }

        public Task WaitUntilOnlineAsync(CancellationToken cancellationToken = default) =>
            IsOnline ? Task.CompletedTask : _online.Task.WaitAsync(cancellationToken);
    }

    private sealed class DroppingPlugin(int failures) : IMusicSourcePlugin
    {
        public int Calls { get; private set; }

        public PluginInfo Info { get; } = new("demo", "Demo", "", "#000000");

        public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
        {
            if (Calls++ < failures)
                throw new HttpRequestException("Connection failure", new IOException("Unable to resolve host \"demo.example\""));

            return Task.FromResult<IReadOnlyList<RemoteTrack>>([new RemoteTrack("demo", "1", "t", "a", null, null)]);
        }

        public Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
