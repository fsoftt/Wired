using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Downloads;
using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Presentation.Formatting;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

public sealed partial class SearchResultViewModel : ObservableObject
{
    private readonly IDownloadQueue _downloads;
    private readonly Func<SearchResultViewModel, Task> _play;
    private readonly TrackCollectionActions _actions;
    private DownloadJob? _job;

    public SearchResultViewModel(
        RemoteTrack track,
        LocalTrack? alreadyDownloaded,
        IDownloadQueue downloads,
        Func<SearchResultViewModel, Task> play,
        TrackCollectionActions actions)
    {
        _actions = actions;
        Track = track;
        _downloads = downloads;
        _play = play;

        LocalTrack = alreadyDownloaded;
        State = alreadyDownloaded is null ? DownloadState.NotDownloaded : DownloadState.Downloaded;

        if (downloads.FindActive(track.SourceId, track.RemoteId) is { } activeJob)
            Apply(activeJob);
    }

    public RemoteTrack Track { get; }

    public string Title => Track.Title;

    public string Artist => Track.Artist;

    public string? ThumbnailUrl => Track.ThumbnailUrl;

    /// <summary>1-based position in a ranked list (e.g. Tendencias); 0 when the result is not ranked.</summary>
    [ObservableProperty]
    public partial int Rank { get; set; }

    public string DurationText => TimeFormatter.Format(Track.Duration);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQueued), nameof(IsDownloading), nameof(IsPending), nameof(IsDownloaded), nameof(CanDownload))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(CancelDownloadCommand))]
    public partial DownloadState State { get; private set; }

    [ObservableProperty]
    public partial double Progress { get; private set; }

    /// <summary>This result is the item being played.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    private partial LocalTrack? LocalTrack { get; set; }

    public bool IsQueued => State == DownloadState.Queued;

    public bool IsDownloading => State == DownloadState.Downloading;

    /// <summary>Queued or downloading: the download can still be canceled.</summary>
    public bool IsPending => IsQueued || IsDownloading;

    public bool IsDownloaded => State == DownloadState.Downloaded;

    public bool CanDownload => State is DownloadState.NotDownloaded or DownloadState.Failed;

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private void Download() => Apply(_downloads.Enqueue(Track));

    [RelayCommand(CanExecute = nameof(IsPending))]
    private void CancelDownload()
    {
        if (_job is not null)
            _downloads.Cancel(_job);
    }

    /// <summary>Plays the downloaded file, or streams the track when it is not on the device.</summary>
    [RelayCommand]
    private Task PlayAsync() => _play(this);

    /// <summary>Swipe action: queue it (streamed if not downloaded).</summary>
    [RelayCommand]
    private Task AddToQueueAsync() => _actions.Queue.AddToQueueAsync(ToPlaybackItem());

    /// <summary>Long press.</summary>
    [RelayCommand]
    private Task ShowMenuAsync()
    {
        if (LocalTrack is not null)
            return _actions.ShowMenuAsync(LocalTrack);

        List<MenuOption> options =
        [
            new("Reproducir a continuación", () => _actions.Queue.PlayNextAsync(ToPlaybackItem())),
            new("Agregar a la cola", () => _actions.Queue.AddToQueueAsync(ToPlaybackItem())),
        ];
        if (CanDownload)
            options.Add(new("Descargar", () => { Download(); return Task.CompletedTask; }));

        return _actions.ShowMenuAsync(Title, options);
    }

    public PlaybackItem ToPlaybackItem() =>
        LocalTrack is not null ? PlaybackItem.FromLocal(LocalTrack) : PlaybackItem.FromStream(Track);

    public bool Represents(PlaybackItem? item) =>
        item is not null && (item.RemoteTrack == Track || item.LocalTrack?.IsSameAs(Track) == true);

    /// <summary>Reflects the state of a download job of this track.</summary>
    public void Apply(DownloadJob job)
    {
        _job = job;
        Progress = job.Progress;

        switch (job.Status)
        {
            case DownloadStatus.Queued:
                State = DownloadState.Queued;
                break;
            case DownloadStatus.Downloading:
                State = DownloadState.Downloading;
                break;
            case DownloadStatus.Completed:
                LocalTrack = job.Result;
                State = DownloadState.Downloaded;
                break;
            case DownloadStatus.Failed:
                State = DownloadState.Failed;
                break;
            case DownloadStatus.Canceled:
                State = DownloadState.NotDownloaded;
                break;
        }
    }

    /// <summary>Keeps the item in sync when the track is deleted from the library.</summary>
    public void MarkAsRemoved()
    {
        LocalTrack = null;
        State = DownloadState.NotDownloaded;
    }
}
