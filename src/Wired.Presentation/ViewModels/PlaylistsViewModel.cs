using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Collections;
using Wired.Core.Import;
using Wired.Core.Plugins;
using Wired.Core.Throttling;
using Wired.Presentation.Collections;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>The "Listas" tab: the "Me gusta" collection plus the user's playlists.</summary>
public sealed partial class PlaylistsViewModel : ObservableObject
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

    public string LikedSummary => _likes.Count == 1 ? "1 canción" : $"{_likes.Count} canciones";

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
            await _dialogs.ShowToastAsync("No hay ninguna fuente de música disponible para importar");
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
                imported = await Task.Run(() => ExportifyCsvParser.Parse(reader, defaultName.Length > 0 ? defaultName : "Importada"));
            }
        }
        catch (FormatException error)
        {
            await _dialogs.ShowErrorAsync("No se pudo importar", error.Message);
            return;
        }
        finally
        {
            IsPreparingImport = false;
        }

        if (imported.Tracks.Count == 0)
        {
            await _dialogs.ShowToastAsync("El archivo no tiene canciones");
            return;
        }

        var source = await ChooseSourceAsync();
        if (source is null)
            return;

        var size = imported.EstimatedBytes / 1_000_000_000d;
        var choice = await _dialogs.ChooseAsync(
            $"Importar {imported.Tracks.Count} canciones (~{(size >= 1 ? $"{size:0.#} GB" : $"{size * 1000:0} MB")})",
            "Cancelar",
            ["Crear una lista nueva", "Agregar a Me gusta", "Lista nueva y Me gusta"]);
        if (choice is not { } destination)
            return;

        var createPlaylist = destination is 0 or 2;
        var name = imported.Name;
        if (createPlaylist)
        {
            var entered = await _dialogs.PromptAsync("Nombre de la lista", "Las canciones se agregarán en el mismo orden del archivo.", "Importar", "Cancelar", name, Playlist.MaxNameLength);
            if (string.IsNullOrWhiteSpace(entered))
                return;
            name = entered.Trim();
        }

        await OfferUnrestrictedBackgroundAsync(imported.Tracks.Count);
        await _imports.StartAsync(imported with { Name = name }, new ImportOptions(source.Info.Id, createPlaylist, AddToLikes: destination is 1 or 2));
        await _dialogs.ShowToastAsync("Importación iniciada: verás el progreso aquí y en Descargas");
    }

    /// <summary>Long imports keep downloading with the screen off only without battery restrictions.</summary>
    private async Task OfferUnrestrictedBackgroundAsync(int trackCount)
    {
        const int LongImport = 20;
        if (trackCount < LongImport || !_backgroundExecution.IsRestricted)
            return;

        var allow = await _dialogs.ConfirmAsync(
            "Descargar con la pantalla apagada",
            "Para que la importación siga mientras el teléfono está bloqueado, permite que Wired funcione sin restricciones de batería.",
            "Permitir",
            "Ahora no");

        if (allow)
            await _backgroundExecution.RequestUnrestrictedAsync();
    }

    private async Task<IMusicSourcePlugin?> ChooseSourceAsync()
    {
        var sources = _plugins.Plugins;
        if (sources.Count <= 1)
            return sources.FirstOrDefault();

        var choice = await _dialogs.ChooseAsync("¿Dónde buscar las canciones?", "Cancelar", sources.Select(p => p.Info.DisplayName).ToList());
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
        return $"En pausa: {source} pidió ir más despacio · sigue a las {until.ToLocalTime():HH:mm}";
    }

    private Task OpenPlaylistAsync(Playlist playlist) => _navigation.OpenPlaylistAsync(playlist.Id);
}
