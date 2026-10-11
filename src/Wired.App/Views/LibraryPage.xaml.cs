using Wired.Presentation.ViewModels;

namespace Wired.App.Views;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _viewModel;
    private CancellationTokenSource? _bubbleCts;

    public LibraryPage(LibraryViewModel viewModel, PlayerViewModel player)
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

    /// <summary>Jumps to the first song of the letter and shows it big for a moment.</summary>
    private async void OnLetterSelected(object? sender, string letter)
    {
        var index = _viewModel.IndexOfLetter(letter);
        if (index >= 0)
            TracksList.ScrollTo(index, position: ScrollToPosition.Start, animate: false);

        LetterBubbleText.Text = letter;
        LetterBubble.Opacity = 1;
        LetterBubble.IsVisible = true;

        _bubbleCts?.Cancel();
        var cts = _bubbleCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(600, cts.Token);
            await LetterBubble.FadeTo(0, 200);
            if (!cts.IsCancellationRequested)
                LetterBubble.IsVisible = false;
        }
        catch (TaskCanceledException)
        {
            // Another letter was chosen meanwhile.
        }
    }
}
