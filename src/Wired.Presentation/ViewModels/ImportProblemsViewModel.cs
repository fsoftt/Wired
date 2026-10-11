using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Import;
using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Core.UseCases;
using Wired.Presentation.Collections;
using Wired.Presentation.Formatting;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>
/// Tracks of an import that were not found (or failed), and a search to pick the right version
/// of each one by hand.
/// </summary>
public sealed partial class ImportProblemsViewModel(
    PlaylistImportService imports,
    SearchTracksUseCase search,
    IPlaybackService playback,
    IDialogService dialogs,
    IUiDispatcher dispatcher) : ObservableObject
{
    private PlaylistImportSession? _session;
    private CancellationTokenSource? _searchCts;
    private bool _isActive;

    public ObservableCollection<ImportProblemViewModel> Problems { get; } = [];

    public ObservableCollection<VersionOptionViewModel> Results { get; } = [];

    [ObservableProperty]
    public partial string Title { get; private set; } = "Sin importar";

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChoosing), nameof(IsListing))]
    public partial ImportProblemViewModel? Selected { get; private set; }

    /// <summary>Searching the version of <see cref="Selected"/>.</summary>
    public bool IsChoosing => Selected is not null;

    public bool IsListing => Selected is null;

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsSearching { get; private set; }

    /// <summary>No results, or why the search failed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchMessage))]
    public partial string? SearchMessage { get; private set; }

    public bool HasSearchMessage => SearchMessage is not null;

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    public async Task OpenAsync(Guid sessionId)
    {
        _session = (await imports.GetSessionsAsync()).FirstOrDefault(s => s.Id == sessionId);
        Selected = null;
        Fill();
    }

    public void Activate()
    {
        if (_isActive)
            return;
        _isActive = true;
        imports.SessionChanged += OnSessionChanged;
    }

    public void Deactivate()
    {
        _isActive = false;
        imports.SessionChanged -= OnSessionChanged;
        _searchCts?.Cancel();
    }

    /// <summary>Back while choosing returns to the list.</summary>
    /// <returns>True when handled.</returns>
    public bool HandleBack()
    {
        if (Selected is null)
            return false;

        CloseChooser();
        return true;
    }

    [RelayCommand]
    private void CloseChooser()
    {
        _searchCts?.Cancel();
        Selected = null;
        Results.Clear();
        SearchMessage = null;
    }

    private async Task SelectAsync(ImportProblemViewModel problem)
    {
        Selected = problem;
        Query = TrackMatcher.SearchQuery(problem.Item.Track);
        await SearchAsync();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (_session is null)
            return;

        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();

        IsSearching = true;
        SearchMessage = null;
        Results.Clear();
        try
        {
            var found = await search.ExecuteAsync(_session.SourceId, Query, cts.Token);
            if (cts.IsCancellationRequested)
                return;

            foreach (var track in found)
                Results.Add(new VersionOptionViewModel(track, PreviewAsync, ChooseAsync));

            if (Results.Count == 0)
                SearchMessage = "Sin resultados. Prueba con otras palabras.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            SearchMessage = ErrorFormatter.Describe(error);
        }
        finally
        {
            if (_searchCts == cts)
                IsSearching = false;
        }
    }

    /// <summary>Copyable list of every track that could not be imported.</summary>
    [RelayCommand]
    private Task CopyListAsync()
    {
        var lines = Problems.Select(p => $"• {p.Title} ({p.Detail})").ToList();
        var preview = string.Join("\n", lines.Take(15)) + (lines.Count > 15 ? $"\n… y {lines.Count - 15} más" : string.Empty);
        return dialogs.ShowErrorAsync($"Sin importar ({lines.Count})", preview, string.Join("\n", lines));
    }

    private Task PreviewAsync(RemoteTrack track) => playback.PlayAsync([PlaybackItem.FromStream(track)]);

    private async Task ChooseAsync(RemoteTrack track)
    {
        if (_session is null || Selected is not { } problem)
            return;

        await imports.ChooseMatchAsync(_session.Id, problem.Item.Index, track);
        CloseChooser();
        await dialogs.ShowToastAsync($"Descargando \"{track.Title}\"");
    }

    private void OnSessionChanged(object? sender, PlaylistImportSession session)
    {
        if (session.Id != _session?.Id || session.Items.Count == 0)
            return;

        dispatcher.Post(() =>
        {
            _session = session;
            Fill();
        });
    }

    private void Fill()
    {
        Title = _session is null ? "Sin importar" : $"Sin importar · {_session.Name}";

        var existing = Problems.ToDictionary(p => p.Item.Index);
        var rows = new List<ImportProblemViewModel>();
        foreach (var item in _session?.Items.Where(i => i.Status is ImportItemStatus.NotFound or ImportItemStatus.Failed) ?? [])
        {
            if (existing.TryGetValue(item.Index, out var row) && row.Item == item)
                rows.Add(row);
            else
                rows.Add(new ImportProblemViewModel(item, SelectAsync));
        }

        Problems.SyncTo(rows);
        IsEmpty = rows.Count == 0;
        Summary = rows.Count switch
        {
            0 => "No queda nada por resolver",
            1 => "1 canción. Toca para elegir la versión correcta.",
            _ => $"{rows.Count} canciones. Toca una para elegir la versión correcta.",
        };
    }
}

public sealed partial class ImportProblemViewModel(ImportItem item, Func<ImportProblemViewModel, Task> select)
{
    public ImportItem Item { get; } = item;

    public string Title => Item.Track.Title;

    public string Artist => string.Join(", ", Item.Track.Artists);

    public string Detail => Item.Status == ImportItemStatus.NotFound ? "No encontrada" : $"Error: {Item.Error}";

    [RelayCommand]
    private Task SelectAsync() => select(this);
}

/// <summary>A search result offered as the version of a track.</summary>
public sealed partial class VersionOptionViewModel(RemoteTrack track, Func<RemoteTrack, Task> preview, Func<RemoteTrack, Task> choose)
{
    public RemoteTrack Track { get; } = track;

    public string Title => Track.Title;

    public string Artist => Track.Artist;

    public string DurationText => TimeFormatter.Format(Track.Duration);

    public string? ThumbnailUrl => Track.ThumbnailUrl;

    [RelayCommand]
    private Task PreviewAsync() => preview(Track);

    [RelayCommand]
    private Task ChooseAsync() => choose(Track);
}
