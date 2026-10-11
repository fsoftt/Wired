using Wired.Core.Models;

namespace Wired.Core.Downloads;

/// <summary>
/// Downloads tracks in the background with a limited number of parallel downloads.
/// </summary>
public interface IDownloadQueue
{
    /// <summary>Snapshot of the jobs, newest first.</summary>
    IReadOnlyList<DownloadJob> Jobs { get; }

    /// <summary>
    /// Raised when a job is added, removed or changes its status or progress.
    /// May be raised from any thread.
    /// </summary>
    event EventHandler<DownloadJobChangedEventArgs>? JobChanged;

    /// <summary>Queues <paramref name="track"/>, or returns the job already downloading it.</summary>
    DownloadJob Enqueue(RemoteTrack track);

    /// <summary>The active (queued or downloading) job for a track, if any.</summary>
    DownloadJob? FindActive(string sourceId, string remoteId);

    void Cancel(DownloadJob job);

    void Retry(DownloadJob job);

    /// <summary>Removes completed, failed and canceled jobs.</summary>
    void ClearFinished();
}

public enum DownloadJobChange
{
    Added,
    Updated,
    Removed,
}

public sealed class DownloadJobChangedEventArgs(DownloadJob job, DownloadJobChange change) : EventArgs
{
    public DownloadJob Job { get; } = job;

    public DownloadJobChange Change { get; } = change;
}
