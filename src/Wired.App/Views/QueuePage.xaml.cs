using Wired.Presentation.ViewModels;

namespace Wired.App.Views;

public partial class QueuePage : ContentPage
{
    private readonly QueueViewModel _viewModel;

    public QueuePage(QueueViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
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

    /// <summary>The list was rearranged by dragging: apply the new order to the queue.</summary>
    private void OnReorderCompleted(object? sender, EventArgs e) => _viewModel.ApplyReorder();

    private async void OnCloseClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
