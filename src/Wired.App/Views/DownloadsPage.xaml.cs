using Wired.Presentation.ViewModels;

namespace Wired.App.Views;

public partial class DownloadsPage : ContentPage
{
    public DownloadsPage(DownloadsViewModel viewModel, PlayerViewModel player)
    {
        InitializeComponent();
        BindingContext = viewModel;
        MiniPlayer.BindingContext = player;
    }
}
