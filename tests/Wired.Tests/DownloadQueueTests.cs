using Wired.Core.Downloads;
using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class DownloadQueueTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly FakePlugin _plugin = new();
    private readonly InMemoryLibrary _library = new();
    private readonly DownloadQueue _queue;

    public DownloadQueueTests() =>
        _queue = new DownloadQueue(
            new DownloadTrackUseCase(new PluginCatalog([_plugin]), _library, _paths, new FixedClock(DateTimeOffset.Now)),
            maxParallelDownloads: 2);

    [Fact]
    public async Task Completes_jobs_and_adds_them_to_the_library()
    {
        var job = _queue.Enqueue(TestData.RemoteTrack("a"));

        await TestWait.UntilAsync(() => job.Status == DownloadStatus.Completed);

        Assert.NotNull(job.Result);
        Assert.Equal(1, job.Progress);
        Assert.Single(_library.Tracks);
    }

    [Fact]
    public async Task Never_exceeds_the_parallel_download_limit()
    {
        _plugin.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobs = Enumerable.Range(0, 5).Select(i => _queue.Enqueue(TestData.RemoteTrack($"t{i}"))).ToList();

        await TestWait.UntilAsync(() => _plugin.RunningDownloads == 2);
        Assert.Equal(3, jobs.Count(j => j.Status == DownloadStatus.Queued));

        _plugin.Gate.SetResult();
        await TestWait.UntilAsync(() => jobs.All(j => j.Status == DownloadStatus.Completed));
        Assert.Equal(2, _plugin.MaxObservedParallelDownloads);
    }

    [Fact]
    public void Enqueuing_a_track_twice_returns_the_active_job()
    {
        _plugin.Gate = new TaskCompletionSource();

        var first = _queue.Enqueue(TestData.RemoteTrack("a"));
        var second = _queue.Enqueue(TestData.RemoteTrack("a"));

        Assert.Same(first, second);
        Assert.Single(_queue.Jobs);
    }

    [Fact]
    public async Task Canceled_jobs_can_be_retried()
    {
        _plugin.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = _queue.Enqueue(TestData.RemoteTrack("a"));
        await TestWait.UntilAsync(() => job.Status == DownloadStatus.Downloading);

        _queue.Cancel(job);
        await TestWait.UntilAsync(() => job.Status == DownloadStatus.Canceled);

        _plugin.Gate.SetResult();
        _queue.Retry(job);
        await TestWait.UntilAsync(() => job.Status == DownloadStatus.Completed);
    }

    [Fact]
    public async Task Failures_are_reported_and_clear_removes_finished_jobs()
    {
        _plugin.DownloadError = new HttpRequestException("offline");
        var job = _queue.Enqueue(TestData.RemoteTrack("a"));

        await TestWait.UntilAsync(() => job.Status == DownloadStatus.Failed);
        Assert.Equal("offline", job.Error);

        _queue.ClearFinished();
        Assert.Empty(_queue.Jobs);
    }

    public void Dispose() => _paths.Dispose();
}
