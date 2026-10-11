using Wired.Presentation.ViewModels;

namespace Wired.App.Views;

/// <summary>
/// Playlist or "Me gusta" detail. Receives the playlist id as a Shell query parameter
/// (empty for "Me gusta").
/// </summary>
public partial class PlaylistDetailPage : ContentPage, IQueryAttributable
{
    public const string PlaylistIdParameter = "playlistId";

    private readonly PlaylistDetailViewModel _viewModel;

    public PlaylistDetailPage(PlaylistDetailViewModel viewModel, PlayerViewModel player)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        MiniPlayer.BindingContext = player;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var id = query.TryGetValue(PlaylistIdParameter, out var value) && Guid.TryParse(value as string, out var parsed)
            ? parsed
            : (Guid?)null;

        _ = _viewModel.OpenAsync(id);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.AppearAsync();
    }

    protected override void OnDisappearing()
    {
        _viewModel.Disappear();
        base.OnDisappearing();
    }

    private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");
}
