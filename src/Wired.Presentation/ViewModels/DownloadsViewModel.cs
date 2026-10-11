using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Downloads;
using Wired.Core.Playback;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>
/// The download queue screen. Also notifies the user when a download fails,
/// so it is created at startup.
/// </summary>
public sealed partial class DownloadsViewModel : ObservableObject
{
    private readonly IDownloadQueue _downloads;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _dispatcher;

    private readonly TrackCollectionActions _actions;
    private readonly IPlaybackService _playback;

    public DownloadsViewModel(
        IDownloadQueue downloads,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        TrackCollectionActions actions,
        IPlaybackService playback)
    {
        _actions = actions;
        _playback = playback;
        _downloads = downloads;
        _dialogs = dialogs;
        _dispatcher = dispatcher;

        foreach (var job in downloads.Jobs)
            Jobs.Add(CreateItem(job));

        downloads.JobChanged += (_, e) => _dispatcher.Post(() => OnJobChanged(e));
    }

    public ObservableCollection<DownloadJobViewModel> Jobs { get; } = [];

    public int ActiveCount => Jobs.Count(j => j.IsActive);

    public bool HasJobs => Jobs.Count > 0;

    public bool IsEmpty => Jobs.Count == 0;

    public bool HasFinished => Jobs.Any(j => !j.IsActive);

    public string Summary => ActiveCount switch
    {
        0 when Jobs.Count == 0 => "Nada por aquí todavía",
        0 => "Todo descargado",
        1 => "1 descarga en curso",
        var count => $"{count} descargas en curso",
    };

    [RelayCommand]
    private void ClearFinished() => _downloads.ClearFinished();

    private DownloadJobViewModel CreateItem(DownloadJob job) => new(job, _downloads, _dialogs, _actions, PlayAsync);

    /// <summary>
    /// Plays the downloaded tracks of the list starting at <paramref name="item"/>. A download still
    /// in progress or failed is streamed on its own.
    /// </summary>
    private Task PlayAsync(DownloadJobViewModel item)
    {
        if (item.Job.Result is null)
            return _playback.PlayAsync([item.ToPlaybackItem()]);

        var playable = Jobs.Where(j => j.Job.Result is not null).ToList();
        return _playback.PlayAsync(playable.Select(j => j.ToPlaybackItem()).ToList(), playable.IndexOf(item));
    }

    private void OnJobChanged(DownloadJobChangedEventArgs e)
    {
        var item = Jobs.FirstOrDefault(j => j.Job.Id == e.Job.Id);

        switch (e.Change)
        {
            case DownloadJobChange.Added when item is null:
                Jobs.Insert(0, CreateItem(e.Job));
                break;
            case DownloadJobChange.Removed when item is not null:
                Jobs.Remove(item);
                break;
            case DownloadJobChange.Updated when item is not null:
                if (item.Refresh() && e.Job.Status == DownloadStatus.Failed)
                    _ = _dialogs.ShowToastAsync($"No se pudo descargar \"{item.Title}\". Revisa la pestaña Descargas.");
                break;
        }

        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasJobs));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasFinished));
        OnPropertyChanged(nameof(Summary));
    }
}
