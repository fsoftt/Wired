using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Collections;
using Wired.Core.Import;
using Wired.Core.Localization;
using Wired.Core.Plugins;
using Wired.Core.Throttling;
using Wired.Presentation.Collections;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>The "Listas" tab: the "Me gusta" collection plus the user's playlists.</summary>
public sealed partial class PlaylistsViewModel : LocalizedObservableObject
{
    private readonly PlaylistService _playlists;
    private readonly LikesService _likes;
    private readonly TrackCollectionActions _actions;
    private readonly INavigationService _navigation;
    private readonly IUiDispatcher _dispatcher;
    private readonly PlaylistImportService _imports;
    private readonly IPluginCatalog _plugins;
    private readonly ICsvFilePicker _filePicker;
    private readonly IDialogService _dialogs;
    private readonly RequestThrottleRegistry _throttles;
    private readonly IBackgroundExecution _backgroundExecution;

    public PlaylistsViewModel(
        PlaylistService playlists,
        LikesService likes,
        TrackCollectionActions actions,
        INavigationService navigation,
        IUiDispatcher dispatcher,
        PlaylistImportService imports,
        IPluginCatalog plugins,
        ICsvFilePicker filePicker,
        IDialogService dialogs,
        RequestThrottleRegistry throttles,
        IBackgroundExecution backgroundExecution)
    {
        _backgroundExecution = backgroundExecution;
        _throttles = throttles;
        _imports = imports;
        _plugins = plugins;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _playlists = playlists;
        _likes = likes;
        _actions = actions;
        _navigation = navigation;
        _dispatcher = dispatcher;

        _playlists.PlaylistsChanged += (_, _) => _dispatcher.Post(() => LoadCommand.Execute(null));
        _likes.LikeChanged += (_, _) => _dispatcher.Post(() => OnPropertyChanged(nameof(LikedSummary)));
        _imports.SessionChanged += (_, session) => _dispatcher.Post(() => OnImportChanged(session));
        _throttles.StateChanged += (_, _) => _dispatcher.Post(RefreshPauses);
    }

    /// <summary>Imports in progress or finished but not dismissed yet.</summary>
    public ObservableCollection<ImportSessionViewModel> Imports { get; } = [];

    [ObservableProperty]
    public partial bool IsPreparingImport { get; private set; }

    public ObservableCollection<PlaylistCardViewModel> Playlists { get; } = [];

    public string LikedSummary => L.N("Songs", _likes.Count);

    public bool HasPlaylists => Playlists.Count > 0;

    [RelayCommand]
    private async Task LoadAsync()
    {
        await _likes.InitializeAsync();
        await LoadImportsAsync();
        var playlists = await _playlists.GetAllAsync();

        // Update cards in place: imports change playlists on every track.
        var existing = Playlists.ToDictionary(c => c.Playlist.Id);
        var target = playlists
            .Select(playlist =>
            {
                if (!existing.TryGetValue(playlist.Id, out var card))
                    return new PlaylistCardViewModel(playlist, OpenPlaylistAsync);

                card.Update(playlist);
                return card;
            })
            .ToList();
        Playlists.SyncTo(target);

        OnPropertyChanged(nameof(HasPlaylists));
        OnPropertyChanged(nameof(LikedSummary));
    }

    [RelayCommand]
    private Task OpenLikedAsync() => _navigation.OpenPlaylistAsync(null);

    [RelayCommand]
    private async Task CreatePlaylistAsync()
    {
        var playlist = await _actions.CreatePlaylistAsync();
        if (playlist is not null)
            await _navigation.OpenPlaylistAsync(playlist.Id);
    }

    /// <summary>
    /// Imports a playlist exported from Spotify with Exportify: each track is searched on a music
    /// source, downloaded and added to a new playlist and/or the likes.
    /// </summary>
    [RelayCommand]
    private async Task ImportAsync()
    {
        if (_plugins.Plugins.Count == 0)
        {
            await _dialogs.ShowToastAsync(L.T("Import_NoSources"));
            return;
        }

        var file = await _filePicker.PickCsvAsync();
        if (file is null)
            return;

        ImportedPlaylist imported;
        IsPreparingImport = true;
        try
        {
            await using (file.Content)
            {
                using var reader = new StreamReader(file.Content);
                var defaultName = Path.GetFileNameWithoutExtension(file.FileName).Replace('_', ' ').Trim();
                imported = await Task.Run(() => ExportifyCsvParser.Parse(reader, defaultName.Length > 0 ? defaultName : L.T("Import_DefaultName")));
            }
        }
        catch (FormatException error)
        {
            await _dialogs.ShowErrorAsync(L.T("Import_FailedTitle"), error.Message);
            return;
        }
        finally
        {
            IsPreparingImport = false;
        }

        if (imported.Tracks.Count == 0)
        {
            await _dialogs.ShowToastAsync(L.T("Import_EmptyFile"));
            return;
        }

        var source = await ChooseSourceAsync();
        if (source is null)
            return;

        var size = imported.EstimatedBytes / 1_000_000_000d;
        var choice = await _dialogs.ChooseAsync(
            L.T("Import_ChooseTitle", imported.Tracks.Count, size >= 1 ? $"{size:0.#} GB" : $"{size * 1000:0} MB"),
            L.T("Common_Cancel"),
            [L.T("Import_DestNew"), L.T("Import_DestLikes"), L.T("Import_DestBoth")]);
        if (choice is not { } destination)
            return;

        var createPlaylist = destination is 0 or 2;
        var name = imported.Name;
        if (createPlaylist)
        {
            var entered = await _dialogs.PromptAsync(L.T("Import_NameTitle"), L.T("Import_NameMessage"), L.T("Common_Import"), L.T("Common_Cancel"), name, Playlist.MaxNameLength);
            if (string.IsNullOrWhiteSpace(entered))
                return;
            name = entered.Trim();
        }

        await OfferUnrestrictedBackgroundAsync(imported.Tracks.Count);
        await _imports.StartAsync(imported with { Name = name }, new ImportOptions(source.Info.Id, createPlaylist, AddToLikes: destination is 1 or 2));
        await _dialogs.ShowToastAsync(L.T("Import_Started"));
    }

    /// <summary>Long imports keep downloading with the screen off only without battery restrictions.</summary>
    private async Task OfferUnrestrictedBackgroundAsync(int trackCount)
    {
        const int LongImport = 20;
        if (trackCount < LongImport || !_backgroundExecution.IsRestricted)
            return;

        var allow = await _dialogs.ConfirmAsync(
            L.T("Import_BackgroundTitle"),
            L.T("Import_BackgroundMessage"),
            L.T("Common_Allow"),
            L.T("Common_NotNow"));

        if (allow)
            await _backgroundExecution.RequestUnrestrictedAsync();
    }

    private async Task<IMusicSourcePlugin?> ChooseSourceAsync()
    {
        var sources = _plugins.Plugins;
        if (sources.Count <= 1)
            return sources.FirstOrDefault();

        var choice = await _dialogs.ChooseAsync(L.T("Import_ChooseSource"), L.T("Common_Cancel"), sources.Select(p => p.Info.DisplayName).ToList());
        return choice is { } index ? sources[index] : null;
    }

    private async Task LoadImportsAsync()
    {
        foreach (var session in await _imports.GetSessionsAsync())
            OnImportChanged(session);
    }

    private void OnImportChanged(PlaylistImportSession session)
    {
        var existing = Imports.FirstOrDefault(i => i.Id == session.Id);

        // Dismissed sessions are reported with no items.
        if (session.Items.Count == 0)
        {
            if (existing is not null)
                Imports.Remove(existing);
            return;
        }

        if (existing is null)
        {
            existing = new ImportSessionViewModel(session, _imports, _navigation);
            Imports.Insert(0, existing);
        }
        else
        {
            existing.Update(session);
        }

        existing.PausedText = PauseTextFor(session.SourceId);
    }

    private void RefreshPauses()
    {
        foreach (var import in Imports)
            import.PausedText = PauseTextFor(import.Session.SourceId);
    }

    private string? PauseTextFor(string sourceId)
    {
        var throttle = _throttles.For(sourceId);
        if (!throttle.IsCoolingDown || throttle.CooldownUntil is not { } until)
            return null;

        var source = _plugins.Plugins.FirstOrDefault(p => p.Info.Id == sourceId)?.Info.DisplayName ?? sourceId;
        return L.T("Import_Paused", source, until.ToLocalTime().ToString("t", CultureInfo.CurrentCulture));
    }

    private Task OpenPlaylistAsync(Playlist playlist) => _navigation.OpenPlaylistAsync(playlist.Id);
}
