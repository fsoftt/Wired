using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Downloads;
using Wired.Core.Localization;
using Wired.Core.Playback;
using Wired.Presentation.Formatting;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

public sealed partial class DownloadJobViewModel(
    DownloadJob job,
    IDownloadQueue downloads,
    IDialogService dialogs,
    TrackCollectionActions actions,
    Func<DownloadJobViewModel, Task> play) : LocalizedObservableObject
{
    private DownloadStatus _lastSeenStatus = job.Status;

    public DownloadJob Job { get; } = job;

    public string Title => Job.Track.Title;

    public string Artist => Job.Track.Artist;

    public string? ThumbnailUrl => Job.Track.ThumbnailUrl;

    public DownloadStatus Status => Job.Status;

    public double Progress => Job.Progress;

    public bool IsActive => Job.IsActive;

    public bool IsDownloading => Job.Status == DownloadStatus.Downloading;

    public bool IsCompleted => Job.Status == DownloadStatus.Completed;

    public bool CanRetry => Job.Status is DownloadStatus.Failed or DownloadStatus.Canceled;

    public string StatusText => Job.Status switch
    {
        DownloadStatus.Queued => L.T("Download_Queued"),
        DownloadStatus.Downloading => L.T("Download_Progress", Job.Progress.ToString("P0", CultureInfo.CurrentCulture)),
        DownloadStatus.Completed => L.T("Download_Completed"),
        DownloadStatus.Failed => L.T("Download_Failed", Job.Error),
        DownloadStatus.Canceled => L.T("Download_Canceled"),
        _ => string.Empty,
    };

    [RelayCommand]
    private void Cancel() => downloads.Cancel(Job);

    [RelayCommand]
    private void Retry() => downloads.Retry(Job);

    /// <summary>Downloaded: plays the file. Otherwise: streams it.</summary>
    public PlaybackItem ToPlaybackItem() =>
        Job.Result is { } local ? PlaybackItem.FromLocal(local) : PlaybackItem.FromStream(Job.Track);

    [RelayCommand]
    private Task PlayAsync() => play(this);

    /// <summary>Swipe action.</summary>
    [RelayCommand]
    private Task AddToQueueAsync() => actions.Queue.AddToQueueAsync(ToPlaybackItem());

    /// <summary>Long press: queue options, plus what fits the download's state.</summary>
    [RelayCommand]
    private Task ShowMenuAsync()
    {
        if (Job.Result is { } local)
            return actions.ShowMenuAsync(local);

        var item = ToPlaybackItem();
        List<MenuOption> options =
        [
            new(L.T("Menu_PlayNext"), () => actions.Queue.PlayNextAsync(item)),
            new(L.T("Menu_AddToQueue"), () => actions.Queue.AddToQueueAsync(item)),
        ];
        if (IsActive)
            options.Add(new(L.T("Menu_CancelDownload"), () => { Cancel(); return Task.CompletedTask; }));
        if (CanRetry)
            options.Add(new(L.T("Common_Retry"), () => { Retry(); return Task.CompletedTask; }));
        if (Job.Failure is not null)
            options.Add(new(L.T("Menu_ViewError"), ShowErrorAsync));

        return actions.ShowMenuAsync(Title, options);
    }

    /// <summary>Shows the full, copyable error of a failed download.</summary>
    [RelayCommand]
    private Task ShowErrorAsync() =>
        Job.Failure is null
            ? Task.CompletedTask
            : dialogs.ShowErrorAsync(
                L.T("Download_FailedTitle"),
                $"{Title}\n\n{ErrorFormatter.Describe(Job.Failure)}",
                $"{Title}\n\n{ErrorFormatter.DescribeForReport(Job.Failure)}");

    /// <summary>Re-reads the job after the queue reported a change.</summary>
    /// <returns>True when the status changed since the last refresh.</returns>
    public bool Refresh()
    {
        var statusChanged = _lastSeenStatus != Job.Status;
        _lastSeenStatus = Job.Status;

        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsDownloading));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(StatusText));
        return statusChanged;
    }
}
