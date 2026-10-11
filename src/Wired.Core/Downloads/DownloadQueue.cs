using Wired.Core.Models;
using Wired.Core.Throttling;
using Wired.Core.UseCases;

namespace Wired.Core.Downloads;

public sealed class DownloadQueue : IDownloadQueue
{
    public const int DefaultMaxParallelDownloads = 2;

    /// <summary>Progress notifications are throttled to steps of this size to avoid flooding the UI.</summary>
    private const double ProgressStep = 0.01;

    private readonly DownloadTrackUseCase _downloadTrack;
    private readonly SemaphoreSlim _slots;
    private readonly Lock _lock = new();
    private readonly List<DownloadJob> _jobs = [];

    public DownloadQueue(DownloadTrackUseCase downloadTrack, int maxParallelDownloads = DefaultMaxParallelDownloads)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelDownloads, 1);

        _downloadTrack = downloadTrack;
        _slots = new SemaphoreSlim(maxParallelDownloads, maxParallelDownloads);
    }

    public event EventHandler<DownloadJobChangedEventArgs>? JobChanged;

    public IReadOnlyList<DownloadJob> Jobs
    {
        get
        {
            lock (_lock)
                return _jobs.ToList();
        }
    }

    public DownloadJob Enqueue(RemoteTrack track)
    {
        DownloadJob job;
        DownloadJob? replaced;

        lock (_lock)
        {
            var active = FindActiveUnsafe(track.SourceId, track.RemoteId);
            if (active is not null)
                return active;

            // A finished job for the same track is superseded by the new one.
            replaced = _jobs.FirstOrDefault(j => j.Targets(track.SourceId, track.RemoteId));
            if (replaced is not null)
                _jobs.Remove(replaced);

            job = new DownloadJob(track);
            _jobs.Insert(0, job);
        }

        if (replaced is not null)
            Notify(replaced, DownloadJobChange.Removed);
        Notify(job, DownloadJobChange.Added);

        _ = RunAsync(job);
        return job;
    }

    public DownloadJob? FindActive(string sourceId, string remoteId)
    {
        lock (_lock)
            return FindActiveUnsafe(sourceId, remoteId);
    }

    public void Cancel(DownloadJob job)
    {
        if (job.IsActive)
            job.Cancellation.Cancel();
    }

    public void Retry(DownloadJob job)
    {
        if (job.Status is not (DownloadStatus.Failed or DownloadStatus.Canceled))
            return;

        lock (_lock)
        {
            if (!_jobs.Contains(job))
                return;

            job.Cancellation.Dispose();
            job.Cancellation = new CancellationTokenSource();
            job.Failure = null;
            job.Progress = 0;
            job.Status = DownloadStatus.Queued;
        }

        Notify(job, DownloadJobChange.Updated);
        _ = RunAsync(job);
    }

    public void ClearFinished()
    {
        List<DownloadJob> removed;
        lock (_lock)
        {
            removed = _jobs.Where(j => !j.IsActive).ToList();
            _jobs.RemoveAll(j => !j.IsActive);
        }

        foreach (var job in removed)
            Notify(job, DownloadJobChange.Removed);
    }

    private DownloadJob? FindActiveUnsafe(string sourceId, string remoteId) =>
        _jobs.FirstOrDefault(j => j.IsActive && j.Targets(sourceId, remoteId));

    private async Task RunAsync(DownloadJob job)
    {
        // Downloads are background work: paced, and paused when the source rate-limits us.
        using var priority = RequestPriority.Background();
        var cancellationToken = job.Cancellation.Token;

        try
        {
            await _slots.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Complete(job, DownloadStatus.Canceled);
            return;
        }

        try
        {
            job.Status = DownloadStatus.Downloading;
            Notify(job, DownloadJobChange.Updated);

            var progress = new ThrottledProgress(value => ReportProgress(job, value));
            job.Result = await Task.Run(() => _downloadTrack.ExecuteAsync(job.Track, progress, cancellationToken), cancellationToken);
            job.Progress = 1;
            Complete(job, DownloadStatus.Completed);
        }
        catch (OperationCanceledException)
        {
            Complete(job, DownloadStatus.Canceled);
        }
        catch (Exception ex)
        {
            job.Failure = ex;
            Complete(job, DownloadStatus.Failed);
        }
        finally
        {
            _slots.Release();
        }
    }

    private void ReportProgress(DownloadJob job, double value)
    {
        job.Progress = value;
        Notify(job, DownloadJobChange.Updated);
    }

    private void Complete(DownloadJob job, DownloadStatus status)
    {
        job.Status = status;
        Notify(job, DownloadJobChange.Updated);
    }

    private void Notify(DownloadJob job, DownloadJobChange change) =>
        JobChanged?.Invoke(this, new DownloadJobChangedEventArgs(job, change));

    /// <summary>
    /// Reports synchronously (unlike <see cref="Progress{T}"/>, which posts to the captured context)
    /// and only when the value moved at least <see cref="ProgressStep"/>.
    /// </summary>
    private sealed class ThrottledProgress(Action<double> report) : IProgress<double>
    {
        private double _lastReported = -1;

        public void Report(double value)
        {
            if (value - _lastReported < ProgressStep && value < 1)
                return;

            _lastReported = value;
            report(value);
        }
    }
}
