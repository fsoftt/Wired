using Wired.Presentation.ViewModels;

namespace Wired.App.Views;

/// <summary>Tracks of an import that were not found. Receives the import id as a Shell query parameter.</summary>
public partial class ImportProblemsPage : ContentPage, IQueryAttributable
{
    public const string SessionIdParameter = "sessionId";

    private readonly ImportProblemsViewModel _viewModel;

    public ImportProblemsPage(ImportProblemsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(SessionIdParameter, out var value) && Guid.TryParse(value as string, out var id))
            _ = _viewModel.OpenAsync(id);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Activate();
    }

    protected override void OnDisappearing()
    {
        _viewModel.Deactivate();
        base.OnDisappearing();
    }

    /// <summary>Back while choosing a version returns to the list first.</summary>
    protected override bool OnBackButtonPressed() => _viewModel.HandleBack() || base.OnBackButtonPressed();

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (!_viewModel.HandleBack())
            await Shell.Current.GoToAsync("..");
    }
}
