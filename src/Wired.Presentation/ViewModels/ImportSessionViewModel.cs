using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Import;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>Progress card of a playlist import in the "Listas" tab.</summary>
public sealed partial class ImportSessionViewModel(
    PlaylistImportSession session,
    PlaylistImportService imports,
    INavigationService navigation) : ObservableObject
{
    public Guid Id => Session.Id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Progress), nameof(ProgressText), nameof(IsRunning), nameof(IsFinished), nameof(HasProblems), nameof(ProblemsText))]
    public partial PlaylistImportSession Session { get; private set; } = session;

    public string Name => Session.Name;

    public double Progress => Session.Total == 0 ? 1 : Session.Finished / (double)Session.Total;

    public string ProgressText
    {
        get
        {
            var text = IsRunning
                ? $"{Session.Done} de {Session.Total} listas"
                : $"Terminada: {Session.Done} de {Session.Total} canciones";

            return Session.AlreadyInLibrary > 0 ? $"{text} · {Session.AlreadyInLibrary} ya estaban" : text;
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
                (_, 0) => $"{notFound} no encontradas · Elegir versión",
                (0, _) => $"{failed} con error · Elegir versión",
                _ => $"{notFound} no encontradas, {failed} con error · Elegir versión",
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
