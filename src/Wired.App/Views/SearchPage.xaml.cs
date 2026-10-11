using Wired.Presentation.ViewModels;

namespace Wired.App.Views;

public partial class SearchPage : ContentPage
{
    public SearchPage(SearchViewModel viewModel, PlayerViewModel player)
    {
        InitializeComponent();
        BindingContext = viewModel;
        MiniPlayer.BindingContext = player;
    }
}
