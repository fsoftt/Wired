using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Import;
using Wired.Core.Localization;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>Progress card of a playlist import in the "Listas" tab.</summary>
public sealed partial class ImportSessionViewModel(
    PlaylistImportSession session,
    PlaylistImportService imports,
    INavigationService navigation) : LocalizedObservableObject
{
    public Guid Id => Session.Id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(ImportingText), nameof(Progress), nameof(ProgressText), nameof(IsRunning), nameof(IsFinished), nameof(HasProblems), nameof(ProblemsText))]
    public partial PlaylistImportSession Session { get; private set; } = session;

    public string Name => Session.Name;

    /// <summary>"Importando «Name»" heading of the card.</summary>
    public string ImportingText => L.T("Playlists_Importing", Name);

    public double Progress => Session.Total == 0 ? 1 : Session.Finished / (double)Session.Total;

    public string ProgressText
    {
        get
        {
            var text = IsRunning
                ? L.T("Import_ProgressRunning", Session.Done, Session.Total)
                : L.T("Import_ProgressFinished", Session.Done, Session.Total);

            return Session.AlreadyInLibrary > 0 ? L.T("Import_AlreadyThere", text, Session.AlreadyInLibrary) : text;
        }
    }

    public bool IsRunning => Session.IsRunning;

    public bool IsFinished => !Session.IsRunning;

    public bool HasProblems => Session.Items.Any(i => i.Status is ImportItemStatus.NotFound or ImportItemStatus.Failed);

    public string ProblemsText
    {
        get
        {
            var notFound = Session.NotFound;
            var failed = Session.Failed;
            return (notFound, failed) switch
            {
                (0, 0) => string.Empty,
                (_, 0) => L.T("Import_ProblemsNotFound", notFound),
                (0, _) => L.T("Import_ProblemsFailed", failed),
                _ => L.T("Import_ProblemsBoth", notFound, failed),
            };
        }
    }

    /// <summary>Set while the source asked us to slow down and the import waits.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    public partial string? PausedText { get; set; }

    public bool IsPaused => PausedText is not null && IsRunning;

    public void Update(PlaylistImportSession updated)
    {
        Session = updated;
        OnPropertyChanged(nameof(IsPaused));
    }

    [RelayCommand]
    private Task CancelAsync() => imports.CancelAsync(Id);

    [RelayCommand]
    private Task DismissAsync() => imports.DismissAsync(Id);

    /// <summary>The tracks that could not be imported, to pick their version by hand.</summary>
    [RelayCommand]
    private Task ShowProblemsAsync() => navigation.OpenImportProblemsAsync(Id);
}
