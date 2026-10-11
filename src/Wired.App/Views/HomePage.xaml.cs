using System.ComponentModel;
using Wired.Presentation.ViewModels;

namespace Wired.App.Views;

public partial class HomePage : ContentPage
{
    private const string PulseAnimation = "HomeSkeletonPulse";

    private readonly HomeViewModel _viewModel;

    public HomePage(HomeViewModel viewModel, PlayerViewModel player)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        MiniPlayer.BindingContext = player;
        SizeChanged += (_, _) => UpdateSizes(Width);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        UpdatePulse();
        try
        {
            await _viewModel.OnAppearingAsync();
        }
        catch (Exception)
        {
            // The sections report their own errors; a failed first load must never crash the app.
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopPulse();
        _viewModel.OnDisappearing();
    }

    /// <summary>Card and column sizes follow the page width: two cards plus a peek, and a column with the next one peeking.</summary>
    private void UpdateSizes(double pageWidth)
    {
        if (pageWidth <= 0)
            return;

        var cardWidth = Math.Clamp(Math.Round(pageWidth * 0.40), 140, 240);
        var columnWidth = Math.Clamp(Math.Round(pageWidth - 48), 240, 440);

        Resources["CardWidth"] = cardWidth;
        Resources["ColumnWidth"] = columnWidth;

        // Cover + title (2 lines) + artist.
        TrendingList.HeightRequest = TrendingSkeleton.HeightRequest = cardWidth + 78;
        TrendingColombiaList.HeightRequest = TrendingColombiaSkeleton.HeightRequest = cardWidth + 78;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(HomeViewModel.TrendingState) or nameof(HomeViewModel.TrendingColombiaState) or nameof(HomeViewModel.ForYouState))
            UpdatePulse();
    }

    /// <summary>The skeleton pulses only while a section is loading with nothing to show; otherwise no animation runs.</summary>
    private void UpdatePulse()
    {
        if (_viewModel.IsTrendingLoading || _viewModel.IsTrendingColombiaLoading || _viewModel.IsForYouLoading)
        {
            if (!this.AnimationIsRunning(PulseAnimation))
            {
                new Animation(t =>
                {
                    var opacity = 1 - 0.55 * Math.Sin(Math.PI * t);
                    TrendingSkeleton.Opacity = opacity;
                    TrendingColombiaSkeleton.Opacity = opacity;
                    ForYouSkeleton.Opacity = opacity;
                }, 0, 1)
                    .Commit(this, PulseAnimation, length: 1400, repeat: () => true);
            }
        }
        else
        {
            StopPulse();
        }
    }

    private void StopPulse()
    {
        this.AbortAnimation(PulseAnimation);
        TrendingSkeleton.Opacity = TrendingColombiaSkeleton.Opacity = ForYouSkeleton.Opacity = 1;
    }
}
