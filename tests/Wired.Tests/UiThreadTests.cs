using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Tests.Fakes;

namespace Wired.Tests;

/// <summary>
/// Regression: Android throws NetworkOnMainThreadException when plugins run on the UI thread,
/// which broke streaming. Calls coming from a UI context must reach plugins on a background thread.
/// </summary>
public class UiThreadTests
{
    [Fact]
    public async Task Streaming_never_runs_the_plugin_on_the_ui_context()
    {
        var plugin = new ContextRecordingPlugin();
        var resolver = new PlaybackSourceResolver(new PluginCatalog([plugin]));

        await RunOnUiContextAsync(() => resolver.ResolveAsync(PlaybackItem.FromStream(TestData.RemoteTrack())));

        Assert.False(plugin.CalledOnUiContext);
    }

    [Fact]
    public async Task Search_never_runs_the_plugin_on_the_ui_context()
    {
        var plugin = new ContextRecordingPlugin();
        var search = new SearchTracksUseCase(new PluginCatalog([plugin]));

        await RunOnUiContextAsync(() => search.ExecuteAsync(plugin.Info.Id, "query"));

        Assert.False(plugin.CalledOnUiContext);
    }

    private static Task RunOnUiContextAsync(Func<Task> action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new FakeUiContext());
        try
        {
            return action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class FakeUiContext : SynchronizationContext;

    private sealed class ContextRecordingPlugin : IStreamableMusicSource
    {
        public bool CalledOnUiContext { get; private set; }

        public PluginInfo Info { get; } = new("fake", "Fake", "", "#000000");

        public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default)
        {
            Record();
            return Task.FromResult<IReadOnlyList<RemoteTrack>>([]);
        }

        public Task<Stream> OpenStreamAsync(RemoteTrack track, CancellationToken cancellationToken = default)
        {
            Record();
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private void Record() => CalledOnUiContext |= SynchronizationContext.Current is FakeUiContext;
    }
}
