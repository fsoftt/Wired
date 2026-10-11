using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Wired.Core.Abstractions;
using Wired.Core.Downloads;
using Wired.Core.Localization;
using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Core.Throttling;
using Wired.Core.UseCases;
using Wired.Presentation.Collections;
using Wired.Presentation.Messages;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>
/// The Home tab: "Tendencias", "Top Colombia" and "Para ti". Each section loads (and fails) on its own and shows its
/// cached copy first, so opening the app does not wait for, or repeat, requests to the sources.
/// </summary>
public sealed partial class HomeViewModel : LocalizedObservableObject, IRecipient<TrackRemovedMessage>
{
    private readonly GetHomeFeedUseCase _feed;
    private readonly IDownloadQueue _downloads;
    private readonly ITrackLibrary _library;
    private readonly IPlaybackService _playback;
    private readonly IUiDispatcher _dispatcher;
    private readonly TrackCollectionActions _actions;

    private readonly IReadOnlyList<string> _sourceNames;

    private CancellationTokenSource? _loadCts;
    private bool _isLoaded;

    public HomeViewModel(
        GetHomeFeedUseCase feed,
        IDownloadQueue downloads,
        ITrackLibrary library,
        IPlaybackService playback,
        IUiDispatcher dispatcher,
        IMessenger messenger,
        TrackCollectionActions actions)
    {
        _feed = feed;
        _sourceNames = feed.DiscoverableSourceNames;
        _downloads = downloads;
        _library = library;
        _playback = playback;
        _dispatcher = dispatcher;
        _actions = actions;

        _downloads.JobChanged += (_, e) => _dispatcher.Post(() => OnDownloadJobChanged(e.Job));
        _playback.StateChanged += (_, _) => _dispatcher.Post(HighlightCurrentTracks);
        messenger.Register(this);
    }

    /// <summary>"Top global · Source" under the section title; just "Top global" when no source can provide charts.</summary>
    public string TrendingSubtitle => WithSources(L.T("Home_TrendingSubtitle"));

    public string TrendingColombiaSubtitle => WithSources(L.T("Home_ColombiaSubtitle"));

    /// <summary>Shown when a chart section is empty: explains the cause when no installed source offers charts.</summary>
    public string TrendingEmptyText => HasDiscoverableSources
        ? L.T("Home_TrendingEmpty")
        : L.T("Home_NoTrendingSources");

    public string TrendingColombiaEmptyText => HasDiscoverableSources
        ? L.T("Home_ColombiaEmpty")
        : L.T("Home_NoTrendingSources");

    public string ForYouEmptyText => HasDiscoverableSources
        ? L.T("Home_ForYouEmpty")
        : L.T("Home_ForYouNoSources");

    private bool HasDiscoverableSources => _sourceNames.Count > 0;

    private string WithSources(string title) => HasDiscoverableSources ? $"{title} · {string.Join(", ", _sourceNames)}" : title;

    public ObservableCollection<SearchResultViewModel> Trending { get; } = [];

    public ObservableCollection<SearchResultViewModel> TrendingColombia { get; } = [];

    public ObservableCollection<SearchResultViewModel> ForYou { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTrendingLoading), nameof(HasTrending), nameof(HasNoTrending), nameof(HasTrendingError))]
    public partial HomeSectionState TrendingState { get; private set; } = HomeSectionState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTrendingColombiaLoading), nameof(HasTrendingColombia), nameof(HasNoTrendingColombia), nameof(HasTrendingColombiaError))]
    public partial HomeSectionState TrendingColombiaState { get; private set; } = HomeSectionState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForYouLoading), nameof(HasForYou), nameof(HasNoForYou), nameof(HasForYouError))]
    public partial HomeSectionState ForYouState { get; private set; } = HomeSectionState.Loading;

    [ObservableProperty]
    public partial string? TrendingErrorMessage { get; private set; }

    [ObservableProperty]
    public partial string? TrendingColombiaErrorMessage { get; private set; }

    [ObservableProperty]
    public partial string? ForYouErrorMessage { get; private set; }

    /// <summary>Short, non-blocking hint shown when a refresh could not happen and cached content is shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string? Notice { get; private set; }

    /// <summary>Pull-to-refresh spinner.</summary>
    [ObservableProperty]
    public partial bool IsRefreshing { get; set; }

    public bool IsTrendingLoading => TrendingState == HomeSectionState.Loading;

    public bool HasTrending => TrendingState == HomeSectionState.Content;

    public bool HasNoTrending => TrendingState == HomeSectionState.Empty;

    public bool HasTrendingError => TrendingState == HomeSectionState.Error;

    public bool IsTrendingColombiaLoading => TrendingColombiaState == HomeSectionState.Loading;

    public bool HasTrendingColombia => TrendingColombiaState == HomeSectionState.Content;

    public bool HasNoTrendingColombia => TrendingColombiaState == HomeSectionState.Empty;

    public bool HasTrendingColombiaError => TrendingColombiaState == HomeSectionState.Error;

    public bool IsForYouLoading => ForYouState == HomeSectionState.Loading;

    public bool HasForYou => ForYouState == HomeSectionState.Content;

    public bool HasNoForYou => ForYouState == HomeSectionState.Empty;

    public bool HasForYouError => ForYouState == HomeSectionState.Error;

    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>
    /// The page was shown. The first time (or while the first load has not finished) the sections load; later
    /// appearances, e.g. coming back to the tab, do nothing.
    /// </summary>
    public Task OnAppearingAsync() =>
        _isLoaded || IsLoading ? Task.CompletedTask : LoadAsync(force: false, HomeSectionKind.Trending, HomeSectionKind.TrendingColombia, HomeSectionKind.ForYou);

    /// <summary>The page was left: stops the requests still running. They start again on the next appearance.</summary>
    public void OnDisappearing() => _loadCts?.Cancel();

    private bool IsLoading => _loadCts is { IsCancellationRequested: false };

    /// <summary>Pull to refresh: fetches all sections again. Ignored while a load is running.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            if (!IsLoading)
                await LoadAsync(force: true, HomeSectionKind.Trending, HomeSectionKind.TrendingColombia, HomeSectionKind.ForYou);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>"Reintentar" of one section: fetches only that one again. Ignored while a load is running.</summary>
    [RelayCommand]
    private Task RetryTrendingAsync() => RetryAsync(HomeSectionKind.Trending);

    /// <inheritdoc cref="RetryTrendingAsync"/>
    [RelayCommand]
    private Task RetryTrendingColombiaAsync() => RetryAsync(HomeSectionKind.TrendingColombia);

    /// <inheritdoc cref="RetryTrendingAsync"/>
    [RelayCommand]
    private Task RetryForYouAsync() => RetryAsync(HomeSectionKind.ForYou);

    private Task RetryAsync(HomeSectionKind kind) =>
        IsLoading ? Task.CompletedTask : LoadAsync(force: true, kind);

    private async Task LoadAsync(bool force, params HomeSectionKind[] kinds)
    {
        var cts = _loadCts = new CancellationTokenSource();
        try
        {
            Notice = null;
            await Task.WhenAll(kinds.Select(kind => LoadSectionAsync(kind, force, cts.Token)));

            // A section that failed is tried again the next time the page appears (the cache TTL still applies).
            _isLoaded = !cts.IsCancellationRequested
                && TrendingState != HomeSectionState.Error
                && TrendingColombiaState != HomeSectionState.Error
                && ForYouState != HomeSectionState.Error;
        }
        finally
        {
            if (ReferenceEquals(_loadCts, cts))
                _loadCts = null;
            cts.Dispose();
        }
    }

    private async Task LoadSectionAsync(HomeSectionKind kind, bool force, CancellationToken cancellationToken)
    {
        try
        {
            // Whatever was saved shows right away; the fetch (if the copy is old) updates it afterwards.
            var section = await _feed.GetCachedAsync(kind, cancellationToken);
            if (section is not null)
                await ShowAsync(kind, section.Tracks, cancellationToken);
            else
                SetState(kind, HomeSectionState.Loading);

            section = await _feed.LoadAsync(kind, force, cancellationToken);
            await ShowAsync(kind, section.Tracks, cancellationToken);

            Notice = section.Issue switch
            {
                HomeFeedIssue.RateLimited => L.T("Home_NoticeRateLimited"),
                HomeFeedIssue.Failed => L.T("Home_NoticeFailed"),
                _ => Notice,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Left the page: keep what is shown, the next appearance loads again.
        }
        catch (Exception error)
        {
            var message = error is SourceRateLimitedException
                ? L.T("Home_ErrorRateLimited")
                : L.T("Home_ErrorConnect");
            switch (kind)
            {
                case HomeSectionKind.Trending:
                    TrendingErrorMessage = message;
                    break;
                case HomeSectionKind.TrendingColombia:
                    TrendingColombiaErrorMessage = message;
                    break;
                default:
                    ForYouErrorMessage = message;
                    break;
            }
            SetState(kind, HomeSectionState.Error);
        }
    }

    private async Task ShowAsync(HomeSectionKind kind, IReadOnlyList<RemoteTrack> tracks, CancellationToken cancellationToken)
    {
        var library = await _library.GetAllAsync(cancellationToken);
        var rows = RowsOf(kind);

        var existing = rows.ToDictionary(r => r.Track);
        rows.SyncTo(tracks
            .DistinctBy(t => (t.SourceId, t.RemoteId))
            .Select(track => existing.GetValueOrDefault(track)
                ?? new SearchResultViewModel(track, library.FirstOrDefault(local => local.IsSameAs(track)), _downloads, result => PlayAsync(rows, result), _actions))
            .ToList());

        if (kind != HomeSectionKind.ForYou)
        {
            for (var i = 0; i < rows.Count; i++)
                rows[i].Rank = i + 1;
        }

        HighlightCurrentTracks();
        SetState(kind, rows.Count > 0 ? HomeSectionState.Content : HomeSectionState.Empty);
    }

    private void SetState(HomeSectionKind kind, HomeSectionState state)
    {
        switch (kind)
        {
            case HomeSectionKind.Trending:
                TrendingState = state;
                break;
            case HomeSectionKind.TrendingColombia:
                TrendingColombiaState = state;
                break;
            default:
                ForYouState = state;
                break;
        }
    }

    private ObservableCollection<SearchResultViewModel> RowsOf(HomeSectionKind kind) => kind switch
    {
        HomeSectionKind.Trending => Trending,
        HomeSectionKind.TrendingColombia => TrendingColombia,
        _ => ForYou,
    };

    /// <summary>Plays the section as a queue starting at <paramref name="result"/>, streaming what is not downloaded.</summary>
    private Task PlayAsync(ObservableCollection<SearchResultViewModel> section, SearchResultViewModel result) =>
        _playback.PlayAsync(section.Select(r => r.ToPlaybackItem()).ToList(), section.IndexOf(result));

    private IEnumerable<SearchResultViewModel> AllRows => Trending.Concat(TrendingColombia).Concat(ForYou);

    private void HighlightCurrentTracks()
    {
        var current = _playback.CurrentItem;
        foreach (var result in AllRows)
            result.IsCurrent = result.Represents(current);
    }

    private void OnDownloadJobChanged(DownloadJob job)
    {
        foreach (var result in AllRows.Where(r => job.Targets(r.Track.SourceId, r.Track.RemoteId)))
            result.Apply(job);
    }

    public void Receive(TrackRemovedMessage message)
    {
        foreach (var result in AllRows.Where(r => message.Track.IsSameAs(r.Track)))
            result.MarkAsRemoved();
    }
}
