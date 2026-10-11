using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Core.Downloads;
using Wired.Core.Enrichment;
using Wired.Core.Localization;
using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Core.UseCases;
using Wired.Presentation.Collections;
using Wired.Presentation.Formatting;
using Wired.Presentation.Messages;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

public sealed partial class LibraryViewModel : TrackListViewModel
{
    private readonly ITrackLibrary _library;
    private readonly RemoveTrackUseCase _removeTrack;
    private readonly RecoverMusicUseCase _recoverMusic;
    private readonly RemoveDuplicateTracksUseCase? _removeDuplicates;
    private readonly IPreferenceStore? _preferences;
    private readonly LanguageSettings? _language;
    private readonly CollectionsBackupService _collectionsBackup;
    private readonly IMusicFolderAccess _musicFolder;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _dispatcher;
    private readonly IMessenger _messenger;

    private readonly List<TrackItemViewModel> _allTracks = [];
    private bool _isLoaded;

    public LibraryViewModel(
        ITrackLibrary library,
        RemoveTrackUseCase removeTrack,
        RecoverMusicUseCase recoverMusic,
        CollectionsBackupService collectionsBackup,
        IMusicFolderAccess musicFolder,
        IPlaybackService playback,
        LikesService likes,
        TrackCollectionActions actions,
        IDownloadQueue downloads,
        IDialogService dialogs,
        IUiDispatcher dispatcher,
        IMessenger messenger,
        LibraryEnrichmentService enrichment,
        RemoveDuplicateTracksUseCase? removeDuplicates = null,
        IPreferenceStore? preferences = null,
        LanguageSettings? language = null)
        : base(playback, likes, actions, dispatcher)
    {
        _preferences = preferences;
        _language = language;
        SortOrder = Enum.TryParse<LibrarySortOrder>(preferences?.Get(SortPreferenceKey), out var saved) && Enum.IsDefined(saved)
            ? saved
            : LibrarySortOrder.Recent;
        _removeDuplicates = removeDuplicates;
        enrichment.TrackUpdated += (_, track) => dispatcher.Post(() => ReplaceTrack(track));
        enrichment.StatusChanged += (_, status) => dispatcher.Post(() => ShowEnrichment(status));
        ShowEnrichment(enrichment.Status);
        _library = library;
        _removeTrack = removeTrack;
        _recoverMusic = recoverMusic;
        _collectionsBackup = collectionsBackup;
        _musicFolder = musicFolder;
        _dialogs = dialogs;
        _dispatcher = dispatcher;
        _messenger = messenger;

        Filter = string.Empty;

        downloads.JobChanged += OnDownloadJobChanged;
        Activate();
    }

    [ObservableProperty]
    public partial string Filter { get; set; }

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    public bool IsEmpty => _isLoaded && _allTracks.Count == 0;

    public bool HasTracks => _allTracks.Count > 0;

    public string Summary => L.N("Songs", _allTracks.Count);

    public string MusicLocation => _musicFolder.DisplayLocation;

    private const string SortPreferenceKey = "library.sort";

    /// <summary>Lists shorter than this scroll fine without the letter index.</summary>
    private const int MinTracksForIndex = 20;

    private static readonly (LibrarySortOrder Order, string LabelKey)[] SortOptions =
    [
        (LibrarySortOrder.Recent, "Sort_Recent"),
        (LibrarySortOrder.Title, "Sort_TitleAZ"),
        (LibrarySortOrder.Artist, "Sort_ArtistAZ"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortLabel), nameof(IsAlphabetical))]
    public partial LibrarySortOrder SortOrder { get; private set; }

    public string SortLabel => L.T(SortOptions.First(o => o.Order == SortOrder).LabelKey);

    public bool IsAlphabetical => SortOrder != LibrarySortOrder.Recent;

    /// <summary>Letters with songs in the visible list, for the fast scroll index.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndexVisible))]
    public partial IReadOnlyList<string> IndexLetters { get; private set; } = [];

    public bool IsIndexVisible => IsAlphabetical && IndexLetters.Count > 1 && Tracks.Count >= MinTracksForIndex;

    [RelayCommand]
    private async Task ChangeSortAsync()
    {
        var choice = await _dialogs.ChooseAsync(L.T("Sort_By"), L.T("Common_Cancel"), SortOptions.Select(o => L.T(o.LabelKey)).ToList());
        if (choice is not { } index || SortOptions[index].Order == SortOrder)
            return;

        SortOrder = SortOptions[index].Order;
        _preferences?.Set(SortPreferenceKey, SortOrder.ToString());
        ApplyFilter();
    }

    /// <summary>Asks which language the interface uses.</summary>
    [RelayCommand]
    private Task ChangeLanguageAsync() => _language?.ChooseAsync() ?? Task.CompletedTask;

    /// <summary>Position of the first visible song under <paramref name="letter"/>, or -1.</summary>
    public int IndexOfLetter(string letter)
    {
        for (var i = 0; i < Tracks.Count; i++)
        {
            if (SortKey.LetterOf(KeyOf(Tracks[i])) == letter)
                return i;
        }

        return -1;
    }

    private string KeyOf(TrackItemViewModel item) => SortKey.For(SortOrder == LibrarySortOrder.Artist ? item.Artist : item.Title);

    /// <summary>"Completando carátulas: 37 de 588", problems found, or empty when there is nothing to tell.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEnrichmentText))]
    public partial string EnrichmentText { get; private set; } = string.Empty;

    public bool HasEnrichmentText => EnrichmentText.Length > 0;

    private IReadOnlyList<string> _enrichmentProblems = [];

    private void ShowEnrichment(EnrichmentStatus status)
    {
        _enrichmentProblems = status.Problems;
        var progress = status.Phase switch
        {
            EnrichmentPhase.Covers when status.Total == 0 => L.T("Enrichment_Checking"),
            EnrichmentPhase.Covers => L.T("Enrichment_Covers", status.Done, status.Total),
            EnrichmentPhase.Loudness => L.T("Enrichment_Loudness", status.Done, status.Total),
            _ => string.Empty,
        };
        var problems = status.Problems.Count switch
        {
            0 => string.Empty,
            var count => L.N("Enrichment_Problems", count),
        };

        EnrichmentText = string.Join("  ·  ", new[] { progress, problems }.Where(text => text.Length > 0));
    }

    /// <summary>Copyable list of what went wrong while completing covers and data.</summary>
    [RelayCommand]
    private Task ShowEnrichmentProblemsAsync()
    {
        if (_enrichmentProblems.Count == 0)
            return Task.CompletedTask;

        var report = string.Join("\n", _enrichmentProblems);
        var preview = string.Join("\n", _enrichmentProblems.TakeLast(10));
        return _dialogs.ShowErrorAsync(L.T("Enrichment_ProblemsTitle", _enrichmentProblems.Count), preview, report);
    }

    protected override IEnumerable<TrackItemViewModel> AllItems => _allTracks;

    partial void OnFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_isLoaded)
            return;

        IsLoading = true;
        try
        {
            await Likes.InitializeAsync();

            // Older versions could add the same song twice: clean up before showing anything.
            if (_removeDuplicates is not null)
                await _removeDuplicates.ExecuteAsync();

            // Silently pick up files left by a previous installation when we can already read them.
            if (await _musicFolder.HasReadAccessAsync())
                await Task.Run(() => _recoverMusic.ExecuteAsync());

            await ReloadAsync();
            _isLoaded = true;
            OnCollectionChanged();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Right after a reinstall the library is empty but the music folder is still there:
    /// offer to bring everything back instead of making the user find the button.
    /// </summary>
    public async Task OfferRecoveryIfNeededAsync()
    {
        if (!_musicFolder.FolderExists || await _musicFolder.HasReadAccessAsync())
            return;

        if ((await _library.GetAllAsync()).Count > 0)
            return;

        var recover = await _dialogs.ConfirmAsync(
            L.T("Recovery_Title"),
            L.T("Recovery_Message", MusicLocation),
            L.T("Common_Recover"),
            L.T("Common_NotNow"));

        if (recover)
            await RecoverCommand.ExecuteAsync(null);
    }

    /// <summary>Rebuilds the library from the music folder (e.g. after reinstalling the app).</summary>
    [RelayCommand]
    private async Task RecoverAsync()
    {
        if (!await _musicFolder.RequestReadAccessAsync())
        {
            await _dialogs.ShowToastAsync(L.T("Recovery_NoPermission"));
            return;
        }

        IsLoading = true;
        try
        {
            // Tracks first: playlists and likes point to them.
            var recovered = await Task.Run(() => _recoverMusic.ExecuteAsync());
            var collections = await Task.Run(() => _collectionsBackup.RestoreAsync());
            await ReloadAsync();
            _isLoaded = true;
            OnCollectionChanged();

            await _dialogs.ShowToastAsync(DescribeRecovery(recovered, collections));
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string DescribeRecovery(int tracks, CollectionsRestoreResult collections)
    {
        var parts = new List<string>();
        if (tracks > 0)
            parts.Add(L.N("Songs", tracks));
        if (collections.Playlists > 0)
            parts.Add(L.N("Playlists", collections.Playlists));
        if (collections.Likes > 0)
            parts.Add(L.N("LikesCount", collections.Likes));

        if (parts.Count > 0)
            return L.T("Recovery_Found", string.Join(", ", parts));

        return _allTracks.Count == 0 ? L.T("Recovery_NothingFound", MusicLocation) : L.T("Recovery_Complete");
    }

    /// <summary>Keeps the row of every track that is still there, so a reload does not rebuild the list.</summary>
    private async Task ReloadAsync()
    {
        var tracks = await _library.GetAllAsync();
        var existing = _allTracks.ToDictionary(t => t.Track.Id);

        _allTracks.Clear();
        foreach (var track in tracks)
        {
            if (existing.TryGetValue(track.Id, out var item))
            {
                item.Update(track);
                _allTracks.Add(item);
            }
            else
            {
                _allTracks.Add(CreateItem(track));
            }
        }
    }

    public override string RemoveLabel => L.T("Library_RemoveFromDevice");

    public override async Task RemoveAsync(TrackItemViewModel item)
    {
        var confirmed = await _dialogs.ConfirmAsync(
            L.T("Library_DeleteTitle"),
            L.T("Library_DeleteMessage", item.Title, MusicLocation),
            L.T("Common_Delete"),
            L.T("Common_Cancel"));

        if (!confirmed)
            return;

        await Playback.ForgetAsync(item.Track.Id);
        await _removeTrack.ExecuteAsync(item.Track);

        _allTracks.Remove(item);
        OnCollectionChanged();
        _messenger.Send(new TrackRemovedMessage(item.Track));
    }

    private void OnDownloadJobChanged(object? sender, DownloadJobChangedEventArgs e)
    {
        if (e.Change == DownloadJobChange.Updated && e.Job is { Status: DownloadStatus.Completed, Result: { } track })
            _dispatcher.Post(() => AddDownloadedTrack(track));
    }

    private void AddDownloadedTrack(LocalTrack track)
    {
        // Not loaded yet: the track will come from the library on first load.
        if (!_isLoaded || _allTracks.Any(t => t.Track.Id == track.Id))
            return;

        _allTracks.Insert(0, CreateItem(track));
        OnCollectionChanged();
    }

    /// <summary>Metadata or cover of a track changed (enrichment, import): show the new version.</summary>
    private void ReplaceTrack(LocalTrack track)
    {
        _allTracks.Find(t => t.Track.Id == track.Id)?.Update(track);
        if (IsAlphabetical)
            ApplyFilter();
    }

    private void OnCollectionChanged()
    {
        ApplyFilter();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasTracks));
        OnPropertyChanged(nameof(Summary));
    }

    private void ApplyFilter()
    {
        var filter = Filter.Trim();
        IReadOnlyList<TrackItemViewModel> visible = filter.Length == 0 ? _allTracks : _allTracks.Where(t => t.Matches(filter)).ToList();

        if (IsAlphabetical)
        {
            // Ties keep the "recent first" order; the second key keeps songs of an artist by title.
            // "#" (numbers, symbols, other alphabets) is one group, at the top like the index shows it.
            visible = visible
                .OrderBy(t => SortKey.LetterOf(KeyOf(t)) == SortKey.Other ? 0 : 1)
                .ThenBy(KeyOf, StringComparer.Ordinal)
                .ThenBy(t => SortKey.For(SortOrder == LibrarySortOrder.Artist ? t.Title : t.Artist), StringComparer.Ordinal)
                .ToList();
            var letters = visible.Select(t => SortKey.LetterOf(KeyOf(t))).Distinct().ToList();
            if (!letters.SequenceEqual(IndexLetters))
                IndexLetters = letters;
        }
        else if (IndexLetters.Count > 0)
        {
            IndexLetters = [];
        }

        // Minimal changes: rebuilding the list would reset the scroll on every new download.
        Tracks.SyncTo(visible);
        HighlightCurrentTrack();
        OnPropertyChanged(nameof(IsIndexVisible));
    }
}

public enum LibrarySortOrder
{
    Recent,
    Title,
    Artist,
}
