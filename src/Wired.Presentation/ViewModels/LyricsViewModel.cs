using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Localization;
using Wired.Core.Lyrics;
using Wired.Core.Playback;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>
/// Lyrics panel of the "Now playing" screen: loads the lyrics of the current item and
/// follows playback to highlight the line being sung.
/// </summary>
public sealed partial class LyricsViewModel : LocalizedObservableObject
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMilliseconds(200);
    private const double OffsetStep = 0.5;

    private readonly LyricsService _lyrics;
    private readonly IPlaybackService _playback;
    private readonly IUiDispatcher _dispatcher;

    private Lyrics? _current;
    private Guid? _loadedItemId;
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _syncCts;
    private bool _isScreenVisible;

    public LyricsViewModel(LyricsService lyrics, IPlaybackService playback, IUiDispatcher dispatcher)
    {
        _lyrics = lyrics;
        _playback = playback;
        _dispatcher = dispatcher;
        PlainText = string.Empty;

        _playback.StateChanged += (_, _) => _dispatcher.Post(OnPlaybackChanged);
    }

    public ObservableCollection<LyricLineViewModel> Lines { get; } = [];

    /// <summary>Whether the panel is shown instead of the cover art.</summary>
    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsSynced), nameof(IsPlain), nameof(IsInstrumental), nameof(IsNotFound), nameof(HasError))]
    public partial LyricsState State { get; private set; }

    [ObservableProperty]
    public partial string PlainText { get; private set; }

    /// <summary>Index of the line being sung; the view scrolls to it.</summary>
    [ObservableProperty]
    public partial int CurrentLineIndex { get; private set; } = -1;

    /// <summary>Manual correction for tracks whose audio starts earlier or later than the lyrics.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OffsetText))]
    public partial double OffsetSeconds { get; private set; }

    public bool IsLoading => State == LyricsState.Loading;

    public bool IsSynced => State == LyricsState.Synced;

    public bool IsPlain => State == LyricsState.Plain;

    public bool IsInstrumental => State == LyricsState.Instrumental;

    public bool IsNotFound => State == LyricsState.NotFound;

    public bool HasError => State == LyricsState.Error;

    public string OffsetText => OffsetSeconds == 0 ? L.T("Lyrics_InSync") : $"{OffsetSeconds:+0.0;-0.0} s";

    [RelayCommand]
    private void Toggle()
    {
        IsOpen = !IsOpen;
        UpdateActivity();
    }

    /// <summary>Lyrics appear later (the song has a longer intro than the lyrics expect).</summary>
    [RelayCommand]
    private void Delay() => ShiftOffset(-OffsetStep);

    /// <summary>Lyrics appear earlier.</summary>
    [RelayCommand]
    private void Advance() => ShiftOffset(OffsetStep);

    [RelayCommand]
    private Task RetryAsync()
    {
        _loadedItemId = null;
        return LoadCurrentAsync();
    }

    public void OnScreenAppearing()
    {
        _isScreenVisible = true;
        UpdateActivity();
    }

    public void OnScreenDisappearing()
    {
        _isScreenVisible = false;
        UpdateActivity();
    }

    private void ShiftOffset(double seconds)
    {
        OffsetSeconds = Math.Round(OffsetSeconds + seconds, 1);
        SyncWithPosition();
    }

    /// <summary>Lyrics are only loaded and followed while the panel is visible.</summary>
    private void UpdateActivity()
    {
        if (IsOpen && _isScreenVisible)
        {
            _ = LoadCurrentAsync();
            StartSync();
        }
        else
        {
            StopSync();
        }
    }

    private void OnPlaybackChanged()
    {
        if (IsOpen && _isScreenVisible)
            _ = LoadCurrentAsync();
    }

    private async Task LoadCurrentAsync()
    {
        var item = _playback.CurrentItem;
        if (item is null || item.Id == _loadedItemId)
            return;

        _loadedItemId = item.Id;
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();

        _current = null;
        Lines.Clear();
        PlainText = string.Empty;
        CurrentLineIndex = -1;
        OffsetSeconds = 0;
        State = LyricsState.Loading;

        try
        {
            var lyrics = await _lyrics.GetAsync(item, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            Show(lyrics);
        }
        catch (OperationCanceledException)
        {
            // Another track started.
        }
        catch (Exception)
        {
            if (!cts.IsCancellationRequested)
            {
                _loadedItemId = null; // Allow retrying.
                State = LyricsState.Error;
            }
        }
    }

    private void Show(Lyrics? lyrics)
    {
        _current = lyrics;

        if (lyrics is null)
        {
            State = LyricsState.NotFound;
        }
        else if (lyrics.IsSynced)
        {
            foreach (var line in lyrics.Lines)
                Lines.Add(new LyricLineViewModel(line, SeekTo));
            State = LyricsState.Synced;
            SyncWithPosition();
        }
        else if (!string.IsNullOrWhiteSpace(lyrics.PlainText))
        {
            PlainText = lyrics.PlainText;
            State = LyricsState.Plain;
        }
        else
        {
            State = lyrics.IsInstrumental ? LyricsState.Instrumental : LyricsState.NotFound;
        }
    }

    private void SeekTo(LyricLineViewModel line)
    {
        var target = line.Line.Time - TimeSpan.FromSeconds(OffsetSeconds);
        _playback.Seek(target < TimeSpan.Zero ? TimeSpan.Zero : target);
        SyncWithPosition();
    }

    private void SyncWithPosition()
    {
        if (_current is not { IsSynced: true })
            return;

        var index = _current.IndexAt(_playback.Position + TimeSpan.FromSeconds(OffsetSeconds));
        if (index == CurrentLineIndex)
            return;

        if (CurrentLineIndex >= 0 && CurrentLineIndex < Lines.Count)
            Lines[CurrentLineIndex].IsActive = false;
        if (index >= 0 && index < Lines.Count)
            Lines[index].IsActive = true;

        CurrentLineIndex = index;
    }

    private void StartSync()
    {
        if (_syncCts is not null)
            return;

        _syncCts = new CancellationTokenSource();
        _ = SyncLoopAsync(_syncCts.Token);
    }

    private void StopSync()
    {
        _syncCts?.Cancel();
        _syncCts = null;
    }

    private async Task SyncLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SyncInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                _dispatcher.Post(SyncWithPosition);
        }
        catch (OperationCanceledException)
        {
            // Panel hidden.
        }
    }
}
