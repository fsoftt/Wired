using Wired.Presentation.ViewModels;

namespace Wired.App.Views;

public partial class PlaylistsPage : ContentPage
{
    private readonly PlaylistsViewModel _viewModel;

    public PlaylistsPage(PlaylistsViewModel viewModel, PlayerViewModel player)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        MiniPlayer.BindingContext = player;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
