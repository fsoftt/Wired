using Wired.Core.Abstractions;
using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Core.Plugins;
using Wired.Core.Throttling;
using Wired.Presentation.Services;

namespace Wired.Tests.Fakes;

internal static class TestData
{
    public static LocalTrack LocalTrack(string title = "Song", string? filePath = null) => new(
        Guid.NewGuid(), title, "Artist", TimeSpan.FromMinutes(3), null,
        filePath ?? $"/music/{title}.m4a", "fake", title, DateTimeOffset.Now);

    public static RemoteTrack RemoteTrack(string id = "abc") =>
        new("fake", id, $"Title {id}", "Artist", TimeSpan.FromMinutes(3), null);
}

internal sealed class FakePlugin(string id = "fake") : IStreamableMusicSource, IDiscoverableMusicSource
{
    public Task<Stream> OpenStreamAsync(RemoteTrack track, CancellationToken cancellationToken = default) =>
        Task.FromResult<Stream>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"stream:{track.RemoteId}")));

    public PluginInfo Info { get; } = new(id, $"Fake {id}", "Fake source", "#000000");

    public List<RemoteTrack> SearchResults { get; } = [];

    public List<RemoteTrack> TrendingResults { get; } = [];

    /// <summary>Regional charts by region code (e.g. "CO"); a region without an entry has no chart.</summary>
    public Dictionary<string, List<RemoteTrack>> RegionalTrendingResults { get; } = [];

    /// <summary>Region of every trending request (null = global).</summary>
    public List<string?> TrendingRegionCalls { get; } = [];

    /// <summary>Makes only the trending requests of a region fail.</summary>
    public Dictionary<string, Exception> RegionalTrendingErrors { get; } = [];

    /// <summary>Related tracks by seed remote id.</summary>
    public Dictionary<string, List<RemoteTrack>> RelatedResults { get; } = [];

    /// <summary>When set, trending and related requests fail with it.</summary>
    public Exception? DiscoveryError { get; set; }

    /// <summary>Number of global (no region) trending requests.</summary>
    public int TrendingCalls { get; private set; }

    /// <summary>Seed of every related request, with whether it ran with background priority.</summary>
    public List<(string RemoteId, bool Background)> RelatedCalls { get; } = [];

    public Task<IReadOnlyList<RemoteTrack>> GetTrendingAsync(string? region, int maxResults, CancellationToken cancellationToken = default)
    {
        TrendingRegionCalls.Add(region);
        if (region is null)
            TrendingCalls++;

        if (DiscoveryError is not null)
            return Task.FromException<IReadOnlyList<RemoteTrack>>(DiscoveryError);

        if (region is not null && RegionalTrendingErrors.TryGetValue(region, out var regionalError))
            return Task.FromException<IReadOnlyList<RemoteTrack>>(regionalError);

        var results = region is null ? TrendingResults : RegionalTrendingResults.GetValueOrDefault(region, []);
        return Task.FromResult<IReadOnlyList<RemoteTrack>>(results.Take(maxResults).ToList());
    }

    public Task<IReadOnlyList<RemoteTrack>> GetRelatedAsync(string remoteId, int maxResults, CancellationToken cancellationToken = default)
    {
        RelatedCalls.Add((remoteId, RequestPriority.IsBackground));
        return DiscoveryError is null
            ? Task.FromResult<IReadOnlyList<RemoteTrack>>(RelatedResults.GetValueOrDefault(remoteId, []).Take(maxResults).ToList())
            : Task.FromException<IReadOnlyList<RemoteTrack>>(DiscoveryError);
    }

    public int DownloadCount { get; private set; }

    public int RunningDownloads;

    public int MaxObservedParallelDownloads;

    /// <summary>When set, downloads wait for it before finishing.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public Exception? DownloadError { get; set; }

    public Task<IReadOnlyList<RemoteTrack>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RemoteTrack>>(SearchResults.Take(maxResults).ToList());

    public async Task<string> DownloadAsync(RemoteTrack track, string destinationDirectory, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        DownloadCount++;
        var running = Interlocked.Increment(ref RunningDownloads);
        InterlockedMax(ref MaxObservedParallelDownloads, running);
        try
        {
            progress?.Report(0.5);
            if (Gate is not null)
                await Gate.Task.WaitAsync(cancellationToken);
            if (DownloadError is not null)
                throw DownloadError;

            var path = Path.Combine(destinationDirectory, $"{track.RemoteId}.m4a");
            await File.WriteAllTextAsync(path, "audio", cancellationToken);
            progress?.Report(1);
            return path;
        }
        finally
        {
            Interlocked.Decrement(ref RunningDownloads);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}

internal static class TestPlayback
{
    public static PlaybackService Create(FakeAudioPlayer player, params IMusicSourcePlugin[] plugins) =>
        new(player, new PlaybackQueue(), new PlaybackSourceResolver(new PluginCatalog(plugins.Length > 0 ? plugins : [new FakePlugin()])));

    public static PlaybackItem Item(string title) => PlaybackItem.FromLocal(TestData.LocalTrack(title));
}

internal sealed class InMemoryLibrary : ITrackLibrary
{
    public List<LocalTrack> Tracks { get; } = [];

    public Task<IReadOnlyList<LocalTrack>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LocalTrack>>(Tracks.ToList());

    public Task<IReadOnlyList<LocalTrack>> GetAllStoredAsync(CancellationToken cancellationToken = default) =>
        GetAllAsync(cancellationToken);

    public Task UpdateAsync(LocalTrack track, CancellationToken cancellationToken = default)
    {
        var index = Tracks.FindIndex(t => t.Id == track.Id);
        if (index >= 0)
            Tracks[index] = track;
        return Task.CompletedTask;
    }

    public Task<LocalTrack?> FindAsync(string sourceId, string remoteId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tracks.FirstOrDefault(t => t.SourceId == sourceId && t.RemoteId == remoteId));

    public Task<LocalTrack> AddAsync(LocalTrack track, CancellationToken cancellationToken = default)
    {
        var existing = Tracks.FirstOrDefault(t => t.SourceId == track.SourceId && t.RemoteId == track.RemoteId);
        if (existing is not null)
            return Task.FromResult(existing);

        Tracks.Add(track);
        return Task.FromResult(track);
    }

    public Task<IReadOnlyDictionary<Guid, Guid>> RemoveDuplicatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(new Dictionary<Guid, Guid>());

    public Task RemoveAsync(Guid trackId, CancellationToken cancellationToken = default)
    {
        Tracks.RemoveAll(t => t.Id == trackId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAudioPlayer : IAudioPlayer
{
    public string? LoadedFile { get; private set; }

    /// <summary>Content of the last stream loaded, read fully like a real player would.</summary>
    public string? LoadedStream { get; private set; }

    public Exception? LoadError { get; set; }

    public bool IsPlaying { get; private set; }

    public TimeSpan Position { get; set; }

    public TimeSpan Duration { get; set; }

    public double GainDb { get; set; }

    public float Volume { get; set; } = 1;

    public event EventHandler? PlaybackEnded;

    public event EventHandler? StateChangedExternally;

    public Task LoadAsync(PlaybackSource source, CancellationToken cancellationToken = default)
    {
        if (LoadError is not null)
            throw LoadError;

        LoadedFile = (source as FilePlaybackSource)?.Path;
        LoadedStream = null;
        if (source is StreamPlaybackSource stream)
        {
            using var reader = new StreamReader(stream.Content);
            LoadedStream = reader.ReadToEnd();
        }

        Position = TimeSpan.Zero;
        return Task.CompletedTask;
    }

    public void Play() => IsPlaying = true;

    public void Pause() => IsPlaying = false;

    public void Stop()
    {
        IsPlaying = false;
        LoadedFile = null;
    }

    public void Seek(TimeSpan position) => Position = position;

    public void Interrupt()
    {
        IsPlaying = false;
        StateChangedExternally?.Invoke(this, EventArgs.Empty);
    }

    public void FinishCurrentTrack()
    {
        IsPlaying = false;
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }
}

internal sealed class TempPaths : IAppPaths, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "wired-tests", Guid.NewGuid().ToString("N"));

    public string MusicDirectory => Path.Combine(_root, "music");

    public string DataDirectory => Path.Combine(_root, "data");

    public string CacheDirectory => Path.Combine(_root, "cache");

    /// <summary>Best effort: a background download a test did not wait for may still be writing here.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Left in the system temp folder.
        }
    }
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; } = now;
}

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

internal sealed class FakeDialogs(bool confirm = true) : IDialogService
{
    public List<string> Toasts { get; } = [];

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) => Task.FromResult(confirm);

    public Task<string?> PromptAsync(string title, string message, string accept, string cancel, string? initialValue = null, int maxLength = -1) =>
        Task.FromResult<string?>(null);

    public Task<int?> ChooseAsync(string title, string cancel, IReadOnlyList<string> options) =>
        Task.FromResult<int?>(null);

    public List<string> Errors { get; } = [];

    public List<(string Message, Func<Task> Action)> Snackbars { get; } = [];

    public Task ShowSnackbarAsync(string message, string actionText, Func<Task> action)
    {
        Snackbars.Add((message, action));
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string title, string message, string? report = null)
    {
        Errors.Add(report ?? message);
        return Task.CompletedTask;
    }

    public Task ShowToastAsync(string message)
    {
        Toasts.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryHomeFeedCache : IHomeFeedCache
{
    public Dictionary<HomeSectionKind, HomeFeedEntry> Entries { get; } = [];

    public Task<HomeFeedEntry?> GetAsync(HomeSectionKind kind, CancellationToken cancellationToken = default) =>
        Task.FromResult(Entries.GetValueOrDefault(kind));

    public Task SetAsync(HomeSectionKind kind, HomeFeedEntry entry, CancellationToken cancellationToken = default)
    {
        Entries[kind] = entry;
        return Task.CompletedTask;
    }
}

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset Now { get; set; } = now;
}
