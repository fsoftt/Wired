using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Wired.Core.Abstractions;
using Wired.Core.Downloads;
using Wired.Core.Localization;
using Wired.Core.Playback;
using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Presentation.Messages;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

public sealed partial class SearchViewModel : LocalizedObservableObject, IRecipient<TrackRemovedMessage>
{
    private readonly SearchTracksUseCase _searchTracks;
    private readonly IDownloadQueue _downloads;
    private readonly ITrackLibrary _library;
    private readonly IPlaybackService _playback;
    private readonly IUiDispatcher _dispatcher;
    private readonly TrackCollectionActions _actions;

    public SearchViewModel(
        IPluginCatalog pluginCatalog,
        SearchTracksUseCase searchTracks,
        IDownloadQueue downloads,
        ITrackLibrary library,
        IPlaybackService playback,
        IUiDispatcher dispatcher,
        IMessenger messenger,
        TrackCollectionActions actions)
    {
        _actions = actions;
        _searchTracks = searchTracks;
        _downloads = downloads;
        _library = library;
        _playback = playback;
        _dispatcher = dispatcher;

        Plugins = new(pluginCatalog.Plugins.Select(p => new PluginOptionViewModel(p.Info, SelectPlugin)));
        Query = string.Empty;
        SelectPlugin(Plugins.FirstOrDefault());

        _downloads.JobChanged += (_, e) => _dispatcher.Post(() => OnDownloadJobChanged(e.Job));
        _playback.StateChanged += (_, _) => _dispatcher.Post(HighlightCurrentResult);
        messenger.Register(this);
    }

    public ObservableCollection<PluginOptionViewModel> Plugins { get; }

    public ObservableCollection<SearchResultViewModel> Results { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchPlaceholder))]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial PluginOptionViewModel? SelectedPlugin { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    public partial string Query { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(ShowIdle), nameof(IsSearching), nameof(HasResults), nameof(HasNoResults), nameof(HasError))]
    public partial SearchState State { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    /// <summary>False when no music source is installed: the page shows an explanation instead of the source chips.</summary>
    public bool HasSources => Plugins.Count > 0;

    public bool HasNoSources => !HasSources;

    /// <summary>The "what do you want to hear" prompt: only meaningful when there is a source to search.</summary>
    public bool ShowIdle => IsIdle && HasSources;

    public string SearchPlaceholder => SelectedPlugin is null
        ? L.T("Search_NoSources")
        : L.T("Search_Placeholder", SelectedPlugin.Name);

    public bool IsIdle => State == SearchState.Idle;

    public bool IsSearching => State == SearchState.Searching;

    public bool HasResults => State == SearchState.Results;

    public bool HasNoResults => State == SearchState.NoResults;

    public bool HasError => State == SearchState.Error;

    private bool CanSearch() => SelectedPlugin is not null && !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanSearch), IncludeCancelCommand = true)]
    private async Task SearchAsync(CancellationToken cancellationToken)
    {
        if (SelectedPlugin is null)
            return;

        State = SearchState.Searching;
        ErrorMessage = null;
        Results.Clear();

        try
        {
            var tracks = await _searchTracks.ExecuteAsync(SelectedPlugin.Id, Query, cancellationToken);
            var library = await _library.GetAllAsync(cancellationToken);

            foreach (var track in tracks)
            {
                var downloaded = library.FirstOrDefault(local => local.IsSameAs(track));
                Results.Add(new SearchResultViewModel(track, downloaded, _downloads, PlayResultAsync, _actions));
            }

            HighlightCurrentResult();
            State = Results.Count > 0 ? SearchState.Results : SearchState.NoResults;
        }
        catch (OperationCanceledException)
        {
            State = SearchState.Idle;
        }
        catch (Exception)
        {
            ErrorMessage = L.T("Search_ErrorConnect", SelectedPlugin.Name);
            State = SearchState.Error;
        }
    }

    [RelayCommand]
    private void SelectPlugin(PluginOptionViewModel? plugin)
    {
        if (plugin is null || plugin == SelectedPlugin)
            return;

        foreach (var option in Plugins)
            option.IsSelected = option == plugin;

        SelectedPlugin = plugin;

        // Results belong to the previous source.
        if (SearchCommand.IsRunning)
            SearchCancelCommand.Execute(null);
        Results.Clear();
        State = SearchState.Idle;
    }

    /// <summary>Plays the results as a queue starting at <paramref name="result"/>, streaming what is not downloaded.</summary>
    private Task PlayResultAsync(SearchResultViewModel result) =>
        _playback.PlayAsync(Results.Select(r => r.ToPlaybackItem()).ToList(), Results.IndexOf(result));

    private void HighlightCurrentResult()
    {
        var current = _playback.CurrentItem;
        foreach (var result in Results)
            result.IsCurrent = result.Represents(current);
    }

    private void OnDownloadJobChanged(DownloadJob job)
    {
        foreach (var result in Results.Where(r => job.Targets(r.Track.SourceId, r.Track.RemoteId)))
            result.Apply(job);
    }

    public void Receive(TrackRemovedMessage message)
    {
        foreach (var result in Results.Where(r => message.Track.IsSameAs(r.Track)))
            result.MarkAsRemoved();
    }
}
