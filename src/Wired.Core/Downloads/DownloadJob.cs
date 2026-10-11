using Wired.Core.Models;

namespace Wired.Core.Downloads;

/// <summary>
/// A track waiting in, or processed by, the <see cref="IDownloadQueue"/>.
/// Its state is only mutated by the queue; subscribe to <see cref="IDownloadQueue.JobChanged"/> to observe it.
/// </summary>
public sealed class DownloadJob
{
    internal DownloadJob(RemoteTrack track) => Track = track;

    public Guid Id { get; } = Guid.NewGuid();

    public RemoteTrack Track { get; }

    public DownloadStatus Status { get; internal set; } = DownloadStatus.Queued;

    /// <summary>Between 0 and 1.</summary>
    public double Progress { get; internal set; }

    public LocalTrack? Result { get; internal set; }

    /// <summary>Why the download failed (<see cref="Exception.Message"/> of <see cref="Failure"/>).</summary>
    public string? Error => Failure?.Message;

    public Exception? Failure { get; internal set; }

    public bool IsActive => Status is DownloadStatus.Queued or DownloadStatus.Downloading;

    internal CancellationTokenSource Cancellation { get; set; } = new();

    public bool Targets(string sourceId, string remoteId) =>
        Track.SourceId == sourceId && Track.RemoteId == remoteId;
}
