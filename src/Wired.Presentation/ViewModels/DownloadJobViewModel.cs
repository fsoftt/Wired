using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Downloads;
using Wired.Core.Playback;
using Wired.Presentation.Formatting;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

public sealed partial class DownloadJobViewModel(
    DownloadJob job,
    IDownloadQueue downloads,
    IDialogService dialogs,
    TrackCollectionActions actions,
    Func<DownloadJobViewModel, Task> play) : ObservableObject
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
        DownloadStatus.Queued => "En cola",
        DownloadStatus.Downloading => $"Descargando · {Job.Progress:P0}",
        DownloadStatus.Completed => "Completada",
        DownloadStatus.Failed => $"Error: {Job.Error} · Mantén presionado para ver detalles",
        DownloadStatus.Canceled => "Cancelada",
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
            new("Reproducir a continuación", () => actions.Queue.PlayNextAsync(item)),
            new("Agregar a la cola", () => actions.Queue.AddToQueueAsync(item)),
        ];
        if (IsActive)
            options.Add(new("Cancelar descarga", () => { Cancel(); return Task.CompletedTask; }));
        if (CanRetry)
            options.Add(new("Reintentar", () => { Retry(); return Task.CompletedTask; }));
        if (Job.Failure is not null)
            options.Add(new("Ver error", ShowErrorAsync));

        return actions.ShowMenuAsync(Title, options);
    }

    /// <summary>Shows the full, copyable error of a failed download.</summary>
    [RelayCommand]
    private Task ShowErrorAsync() =>
        Job.Failure is null
            ? Task.CompletedTask
            : dialogs.ShowErrorAsync(
                "No se pudo descargar",
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
