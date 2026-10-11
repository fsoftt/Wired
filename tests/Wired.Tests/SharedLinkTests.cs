using Wired.Core.Abstractions;
using Wired.Core.Downloads;
using Wired.Core.Models;
using Wired.Core.Plugins;
using Wired.Core.Resilience;
using Wired.Core.Throttling;
using Wired.Core.UseCases;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class SharedLinkTests : IDisposable
{
    private static readonly ThrottleOptions Fast = new()
    {
        BackgroundInterval = TimeSpan.FromMilliseconds(100),
        BackgroundJitter = TimeSpan.Zero,
        InteractiveInterval = TimeSpan.Zero,
        InitialCooldown = TimeSpan.FromMilliseconds(200),
        MaxCooldown = TimeSpan.FromMilliseconds(500),
    };

    private readonly TempPaths _paths = new();
    private readonly InMemoryLibrary _library = new();
    private readonly LinkPlugin _plugin = new();
    private readonly DownloadQueue _queue;
    private readonly DownloadSharedLinkUseCase _useCase;

    public SharedLinkTests()
    {
        _queue = new DownloadQueue(
            new DownloadTrackUseCase(new PluginCatalog([_plugin]), _library, _paths, new FixedClock(DateTimeOffset.Now)));
        _useCase = new DownloadSharedLinkUseCase(new PluginCatalog([_plugin]), _queue);
    }

    [Fact]
    public void FindLinks_finds_the_link_inside_shared_text()
    {
        var links = DownloadSharedLinkUseCase.FindLinks("Mira esto: https://demo.example/abc123?si=XYZ ¡buenísima!").ToList();

        Assert.Equal([new Uri("https://demo.example/abc123?si=XYZ")], links);
    }

    [Fact]
    public void FindLinks_trims_trailing_punctuation()
    {
        var links = DownloadSharedLinkUseCase.FindLinks("Escúchala https://demo.example/abc.").ToList();

        Assert.Equal([new Uri("https://demo.example/abc")], links);
    }

    [Fact]
    public void FindLinks_returns_nothing_for_text_without_links()
    {
        Assert.Empty(DownloadSharedLinkUseCase.FindLinks("Solo texto, sin enlaces por aquí."));
    }

    [Fact]
    public async Task ExecuteAsync_with_a_demo_link_enqueues_and_downloads_the_track()
    {
        var track = await _useCase.ExecuteAsync("Mira: https://demo.example/abc123");

        Assert.NotNull(track);
        Assert.Equal("abc123", track.RemoteId);
        Assert.Contains(_queue.Jobs, j => j.Track == track);

        var job = _queue.Jobs.Single(j => j.Track == track);
        await TestWait.UntilAsync(() => job.Status == DownloadStatus.Completed);

        Assert.Single(_library.Tracks);
    }

    [Fact]
    public async Task ExecuteAsync_with_an_unrelated_link_returns_null_and_enqueues_nothing()
    {
        var track = await _useCase.ExecuteAsync("Mira https://example.com/x");

        Assert.Null(track);
        Assert.Empty(_queue.Jobs);
        Assert.Equal([new Uri("https://example.com/x")], _plugin.Asked);
    }

    [Fact]
    public async Task ExecuteAsync_uses_the_second_link_when_only_it_is_recognized()
    {
        var track = await _useCase.ExecuteAsync("https://example.com/a y también https://demo.example/xyz");

        Assert.NotNull(track);
        Assert.Equal("xyz", track.RemoteId);
        Assert.Equal(2, _plugin.Asked.Count);
    }

    [Fact]
    public async Task Plugin_that_does_not_override_ResolveLinkAsync_returns_null()
    {
        IMusicSourcePlugin plugin = new NoLinkPlugin();

        var track = await plugin.ResolveLinkAsync(new Uri("https://demo.example/abc"));

        Assert.Null(track);
    }

    [Fact]
    public async Task Throttled_wrapper_forwards_ResolveLinkAsync_to_the_inner_plugin()
    {
        var inner = new LinkPlugin();
        IMusicSourcePlugin wrapped = ThrottledMusicSource.Wrap(inner, new RequestThrottle("demo", Fast));

        var track = await wrapped.ResolveLinkAsync(new Uri("https://demo.example/abc123"));

        Assert.NotNull(track);
        Assert.Equal("abc123", track.RemoteId);
        Assert.Single(inner.Asked);
    }

    [Fact]
    public async Task Network_resilient_wrapper_forwards_ResolveLinkAsync_to_the_inner_plugin()
    {
        var inner = new LinkPlugin();
        IMusicSourcePlugin wrapped = NetworkResilientMusicSource.Wrap(inner, new AlwaysOnlineNetworkMonitor(), new ResilienceOptions());

        var track = await wrapped.ResolveLinkAsync(new Uri("https://demo.example/abc123"));

        Assert.NotNull(track);
        Assert.Equal("abc123", track.RemoteId);
        Assert.Single(inner.Asked);
    }

    public void Dispose() => _paths.Dispose();

    /// <summary>Recognizes only Demo links and records every link it was asked about.</summary>
    private sealed class LinkPlugin : IMusicSourcePlugin
    {
        public List<Uri> Asked { get; } = [];

        public PluginInfo Info { get; } = new("demo", "Demo", "Test source", "#FF0000");

        public Task<RemoteTrack?> ResolveLinkAsync(Uri link, CancellationToken cancellationToken = default)
        {
            Asked.Add(link);
            if (!link.Host.Contains("demo", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult<RemoteTrack?>(null);

            var id = link.Segments.Last().TrimEnd('/');
            return Task.FromResult<RemoteTrack?>(new RemoteTrack("demo", id, $"Title {id}", "Artist", TimeSpan.FromMinutes(3), null));
        }

        public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteTrack>>([]);

        public async Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(destinationDirectory, $"{track.RemoteId}.m4a");
            await File.WriteAllTextAsync(path, "audio", cancellationToken);
            progress?.Report(1);
            return path;
        }
    }

    /// <summary>Does not override ResolveLinkAsync, so the interface default (null) applies.</summary>
    private sealed class NoLinkPlugin : IMusicSourcePlugin
    {
        public PluginInfo Info { get; } = new("none", "None", "Test source", "#000000");

        public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RemoteTrack>>([]);

        public Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
