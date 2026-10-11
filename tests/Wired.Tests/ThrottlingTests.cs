using System.Diagnostics;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.Throttling;

namespace Wired.Tests;

public class ThrottlingTests
{
    private static readonly ThrottleOptions Fast = new()
    {
        BackgroundInterval = TimeSpan.FromMilliseconds(100),
        BackgroundJitter = TimeSpan.Zero,
        InteractiveInterval = TimeSpan.Zero,
        InitialCooldown = TimeSpan.FromMilliseconds(200),
        MaxCooldown = TimeSpan.FromMilliseconds(500),
    };

    [Fact]
    public async Task Background_requests_are_spaced()
    {
        var throttle = new RequestThrottle("demo", Fast);
        var watch = Stopwatch.StartNew();

        using (RequestPriority.Background())
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => throttle.WaitTurnAsync()));

        // First is immediate, the other three wait one interval each.
        Assert.True(watch.ElapsedMilliseconds >= 280, $"took {watch.ElapsedMilliseconds} ms");
    }

    [Fact]
    public async Task Interactive_requests_skip_the_background_line_and_cooldowns()
    {
        var throttle = new RequestThrottle("demo", Fast with { InitialCooldown = TimeSpan.FromSeconds(30) });
        throttle.ReportRateLimited(null);

        using (RequestPriority.Background())
            _ = throttle.WaitTurnAsync(); // Stuck until the cooldown ends.

        var watch = Stopwatch.StartNew();
        await throttle.WaitTurnAsync();

        Assert.True(watch.ElapsedMilliseconds < 100);
        Assert.True(throttle.IsCoolingDown);
    }

    [Fact]
    public async Task Background_calls_wait_out_a_rate_limit_and_retry()
    {
        var throttle = new RequestThrottle("demo", Fast);
        var plugin = new FlakyPlugin(failuresBeforeSuccess: 1);
        var source = ThrottledMusicSource.Wrap(plugin, throttle);

        var watch = Stopwatch.StartNew();
        IReadOnlyList<RemoteTrack> results;
        using (RequestPriority.Background())
            results = await source.SearchAsync("q", 5);

        Assert.Single(results);
        Assert.Equal(2, plugin.Calls);
        Assert.True(watch.ElapsedMilliseconds >= 180, "waited for the cooldown");
    }

    [Fact]
    public async Task Interactive_calls_report_a_rate_limit_immediately()
    {
        var source = ThrottledMusicSource.Wrap(new FlakyPlugin(failuresBeforeSuccess: 5), new RequestThrottle("demo", Fast));

        await Assert.ThrowsAsync<SourceRateLimitedException>(() => source.SearchAsync("q", 5));
    }

    [Fact]
    public void Cooldowns_grow_exponentially_up_to_the_maximum_and_reset_on_success()
    {
        var time = new ManualTime();
        var throttle = new RequestThrottle("demo", Fast, time);

        Assert.Equal([200, 400, 500, 500], Enumerable.Range(0, 4).Select(_ => CooldownMs(throttle, time)));

        time.Advance(TimeSpan.FromSeconds(1));
        throttle.ReportSuccess();
        Assert.False(throttle.IsCoolingDown);
        Assert.Equal(200, CooldownMs(throttle, time));
    }

    [Fact]
    public void A_server_requested_delay_wins()
    {
        var time = new ManualTime();
        var throttle = new RequestThrottle("demo", Fast, time);

        throttle.ReportRateLimited(TimeSpan.FromMinutes(3));

        Assert.Equal(time.GetUtcNow() + TimeSpan.FromMinutes(3), throttle.CooldownUntil);
    }

    [Fact]
    public async Task Priority_flows_into_background_threads()
    {
        bool seen;
        using (RequestPriority.Background())
            seen = await Task.Run(() => RequestPriority.IsBackground);

        Assert.True(seen);
        Assert.False(RequestPriority.IsBackground);
    }

    [Fact]
    public void Wrapping_keeps_the_streaming_capability()
    {
        Assert.IsAssignableFrom<IStreamableMusicSource>(ThrottledMusicSource.Wrap(new Fakes.FakePlugin(), new RequestThrottle("fake", Fast)));
        Assert.IsNotAssignableFrom<IStreamableMusicSource>(ThrottledMusicSource.Wrap(new FlakyPlugin(0), new RequestThrottle("x", Fast)));
    }

    private static int CooldownMs(RequestThrottle throttle, ManualTime time)
    {
        throttle.ReportRateLimited(null);
        return (int)(throttle.CooldownUntil!.Value - time.GetUtcNow()).TotalMilliseconds;
    }

    private sealed class FlakyPlugin(int failuresBeforeSuccess) : IMusicSourcePlugin
    {
        public int Calls { get; private set; }

        public PluginInfo Info { get; } = new("demo", "Demo", "", "#000000");

        public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
        {
            if (Calls++ < failuresBeforeSuccess)
                throw new SourceRateLimitedException("slow down");

            return Task.FromResult<IReadOnlyList<RemoteTrack>>([new RemoteTrack("demo", "1", "t", "a", null, null)]);
        }

        public Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
