using System.ComponentModel;
using Wired.Presentation.ViewModels;

namespace Wired.App.Views;

public partial class NowPlayingPage : ContentPage
{
    private readonly LyricsViewModel _lyrics;

    public NowPlayingPage(PlayerViewModel player)
    {
        InitializeComponent();
        BindingContext = player;
        _lyrics = player.Lyrics;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _lyrics.PropertyChanged += OnLyricsPropertyChanged;
        _lyrics.OnScreenAppearing();
    }

    protected override void OnDisappearing()
    {
        _lyrics.OnScreenDisappearing();
        _lyrics.PropertyChanged -= OnLyricsPropertyChanged;
        base.OnDisappearing();
    }

    /// <summary>Keeps the line being sung in the middle of the screen.</summary>
    private void OnLyricsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricsViewModel.CurrentLineIndex) && _lyrics.CurrentLineIndex >= 0)
            LyricsList.ScrollTo(_lyrics.CurrentLineIndex, position: ScrollToPosition.Center, animate: true);
    }

    /// <summary>Keeps the cover art square whatever the screen size.</summary>
    private void OnArtworkSizeChanged(object? sender, EventArgs e)
    {
        if (sender is View artwork && artwork.Width > 0)
            artwork.HeightRequest = artwork.Width;
    }
}
