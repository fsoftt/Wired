using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Localization;
using Wired.Core.Playback;
using Wired.Presentation.Formatting;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>
/// Shared by the mini player bar and the "Now playing" screen.
/// </summary>
public sealed partial class PlayerViewModel : LocalizedObservableObject, IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    private readonly IPlaybackService _playback;
    private readonly LikesService _likes;
    private readonly TrackCollectionActions _actions;
    private readonly IDownloadQueue _downloads;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly SleepTimer? _sleepTimer;
    private readonly CancellationTokenSource _disposeCts = new();

    private bool _isSeeking;

    public PlayerViewModel(
        IPlaybackService playback,
        LikesService likes,
        TrackCollectionActions actions,
        IDownloadQueue downloads,
        INavigationService navigation,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        LyricsViewModel lyrics,
        SleepTimer? sleepTimer = null)
    {
        Lyrics = lyrics;
        _sleepTimer = sleepTimer;
        if (_sleepTimer is not null)
            _sleepTimer.Changed += OnSleepTimerChanged;
        _playback = playback;
        _likes = likes;
        _actions = actions;
        _downloads = downloads;
        _navigation = navigation;
        _dialogs = dialogs;
        _dispatcher = dispatcher;

        _playback.StateChanged += OnPlaybackStateChanged;
        _playback.PlaybackFailed += OnPlaybackFailed;
        _likes.LikeChanged += OnLikeChanged;
        _ = RefreshPositionLoopAsync(_disposeCts.Token);
    }

    /// <summary>Lyrics panel of the "Now playing" screen.</summary>
    public LyricsViewModel Lyrics { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTrack), nameof(Title), nameof(Artist), nameof(ThumbnailUrl), nameof(IsStream), nameof(IsLocal))]
    [NotifyCanExecuteChangedFor(
        nameof(PreviousCommand), nameof(OpenNowPlayingCommand), nameof(ToggleLikeCommand),
        nameof(AddToPlaylistCommand), nameof(DownloadCommand), nameof(OpenQueueCommand))]
    public partial PlaybackItem? CurrentItem { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlay), nameof(ShowPause))]
    public partial bool IsPlaying { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPlay), nameof(ShowPause))]
    public partial bool IsLoading { get; private set; }

    [ObservableProperty]
    public partial bool IsLiked { get; private set; }

    [ObservableProperty]
    public partial bool IsShuffleEnabled { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRepeatOff), nameof(IsRepeatAll), nameof(IsRepeatOne))]
    public partial RepeatMode RepeatMode { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PositionText), nameof(Progress))]
    public partial double PositionSeconds { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText), nameof(Progress), nameof(SeekMaximum))]
    public partial double DurationSeconds { get; private set; }

    public bool HasTrack => CurrentItem is not null;

    /// <summary>Played from the network, not from the device.</summary>
    public bool IsStream => CurrentItem?.IsStream == true;

    /// <summary>Downloaded: can be liked and added to playlists.</summary>
    public bool IsLocal => CurrentItem is { IsStream: false };

    public string Title => CurrentItem?.Title ?? string.Empty;

    public string Artist => CurrentItem?.Artist ?? string.Empty;

    public string? ThumbnailUrl => CurrentItem?.ThumbnailUrl;

    public bool ShowPlay => !IsPlaying && !IsLoading;

    public bool ShowPause => IsPlaying && !IsLoading;

    public bool IsRepeatOff => RepeatMode == RepeatMode.Off;

    public bool IsRepeatAll => RepeatMode == RepeatMode.All;

    public bool IsRepeatOne => RepeatMode == RepeatMode.One;

    public string PositionText => TimeFormatter.Format(TimeSpan.FromSeconds(PositionSeconds));

    public string DurationText => TimeFormatter.Format(TimeSpan.FromSeconds(DurationSeconds));

    /// <summary>Upper bound for the seek slider; never zero so sliders stay valid before a track loads.</summary>
    public double SeekMaximum => Math.Max(1, DurationSeconds);

    /// <summary>Playback progress between 0 and 1.</summary>
    public double Progress => DurationSeconds > 0 ? Math.Clamp(PositionSeconds / DurationSeconds, 0, 1) : 0;

    [RelayCommand]
    private void TogglePlayPause() => _playback.TogglePlayPause();

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private Task NextAsync() => _playback.NextAsync();

    private bool CanGoNext() => _playback.HasNext;

    [RelayCommand(CanExecute = nameof(HasTrack))]
    private Task PreviousAsync() => _playback.PreviousAsync();

    [RelayCommand]
    private void ToggleShuffle() => _playback.SetShuffle(!_playback.IsShuffleEnabled);

    /// <summary>Off → all → one → off.</summary>
    [RelayCommand]
    private void CycleRepeat() => _playback.SetRepeatMode(_playback.RepeatMode switch
    {
        RepeatMode.Off => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        _ => RepeatMode.Off,
    });

    [RelayCommand]
    private void BeginSeek() => _isSeeking = true;

    [RelayCommand]
    private void EndSeek()
    {
        _playback.Seek(TimeSpan.FromSeconds(PositionSeconds));
        _isSeeking = false;
    }

    [RelayCommand(CanExecute = nameof(IsLocal))]
    private Task ToggleLikeAsync() =>
        CurrentItem?.LocalTrack is { } track ? _actions.ToggleLikeAsync(track) : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(IsLocal))]
    private Task AddToPlaylistAsync() =>
        CurrentItem?.LocalTrack is { } track ? _actions.AddToPlaylistAsync(track) : Task.CompletedTask;

    /// <summary>Saves the streamed track to the device.</summary>
    [RelayCommand(CanExecute = nameof(IsStream))]
    private async Task DownloadAsync()
    {
        if (CurrentItem?.RemoteTrack is not { } track)
            return;

        _downloads.Enqueue(track);
        await _dialogs.ShowToastAsync(L.T("Download_Starting", track.Title));
    }

    [RelayCommand(CanExecute = nameof(HasTrack))]
    private Task OpenNowPlayingAsync() => _navigation.OpenNowPlayingAsync();

    [RelayCommand]
    private Task CloseNowPlayingAsync() => _navigation.GoBackAsync();

    [RelayCommand(CanExecute = nameof(HasTrack))]
    private Task OpenQueueAsync() => _navigation.OpenQueueAsync();

    private static readonly (Func<string> Label, TimeSpan Duration)[] SleepOptions =
    [
        (() => L.T("Sleep_Minutes", 15), TimeSpan.FromMinutes(15)),
        (() => L.T("Sleep_Minutes", 30), TimeSpan.FromMinutes(30)),
        (() => L.T("Sleep_Minutes", 45), TimeSpan.FromMinutes(45)),
        (() => L.T("Sleep_OneHour"), TimeSpan.FromHours(1)),
    ];

    [ObservableProperty]
    public partial bool IsSleepTimerActive { get; private set; }

    /// <summary>"Se pausa en 12:30" or at the end of the song; empty when off.</summary>
    [ObservableProperty]
    public partial string SleepTimerText { get; private set; } = string.Empty;

    [RelayCommand]
    private async Task SetSleepTimerAsync()
    {
        if (_sleepTimer is null)
            return;

        var endOfTrackOption = L.T("Sleep_EndOfTrack");
        var options = SleepOptions.Select(o => o.Label()).Append(endOfTrackOption).ToList();
        if (_sleepTimer.IsActive)
            options.Add(L.T("Sleep_TurnOff"));

        var title = _sleepTimer.IsActive ? SleepTimerText : L.T("Sleep_Title");
        if (await _dialogs.ChooseAsync(title, L.T("Common_Cancel"), options) is not { } choice)
            return;

        if (choice < SleepOptions.Length)
            _sleepTimer.Start(SleepOptions[choice].Duration);
        else if (options[choice] == endOfTrackOption)
            _sleepTimer.StartEndOfTrack();
        else
            _sleepTimer.Cancel();
    }

    private void OnSleepTimerChanged(object? sender, EventArgs e) => _dispatcher.Post(SyncSleepTimer);

    private void SyncSleepTimer()
    {
        if (_sleepTimer is null)
            return;

        IsSleepTimerActive = _sleepTimer.IsActive;
        SleepTimerText = _sleepTimer switch
        {
            { IsEndOfTrack: true } => L.T("Sleep_PausesAtEnd"),
            { Remaining: { } left } => L.T("Sleep_PausesIn", left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"m\:ss")),
            _ => string.Empty,
        };
    }

    public void Dispose()
    {
        if (_sleepTimer is not null)
            _sleepTimer.Changed -= OnSleepTimerChanged;
        _playback.StateChanged -= OnPlaybackStateChanged;
        _playback.PlaybackFailed -= OnPlaybackFailed;
        _likes.LikeChanged -= OnLikeChanged;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e) => _dispatcher.Post(SyncWithPlayback);

    private void OnPlaybackFailed(object? sender, PlaybackFailedEventArgs e) =>
        _dispatcher.Post(() => _ = _dialogs.ShowErrorAsync(
            L.T("Playback_Failed"),
            $"{e.Item.Title}\n\n{ErrorFormatter.Describe(e.Error)}",
            $"{e.Item.Title}\n\n{ErrorFormatter.DescribeForReport(e.Error)}"));

    private void OnLikeChanged(object? sender, LikeChangedEventArgs e)
    {
        if (e.TrackId == CurrentItem?.LocalTrack?.Id)
            _dispatcher.Post(() => IsLiked = e.IsLiked);
    }

    private void SyncWithPlayback()
    {
        CurrentItem = _playback.CurrentItem;
        IsPlaying = _playback.IsPlaying;
        IsLoading = _playback.IsLoading;
        IsShuffleEnabled = _playback.IsShuffleEnabled;
        RepeatMode = _playback.RepeatMode;
        IsLiked = CurrentItem?.LocalTrack is { } track && _likes.IsLiked(track.Id);
        RefreshPosition();

        NextCommand.NotifyCanExecuteChanged();
    }

    private void RefreshPosition()
    {
        DurationSeconds = _playback.Duration.TotalSeconds;
        if (!_isSeeking)
            PositionSeconds = _playback.Position.TotalSeconds;
    }

    private async Task RefreshPositionLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (_playback.IsPlaying)
                    _dispatcher.Post(RefreshPosition);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }
}
