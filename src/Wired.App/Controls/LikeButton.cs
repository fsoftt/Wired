using System.Windows.Input;

namespace Wired.App.Controls;

/// <summary>
/// Heart toggle: outlined when not liked, filled pink when liked.
/// </summary>
public sealed class LikeButton : ContentView
{
    public static readonly BindableProperty IsLikedProperty = BindableProperty.Create(
        nameof(IsLiked), typeof(bool), typeof(LikeButton), false,
        propertyChanged: (bindable, _, _) => ((LikeButton)bindable).UpdateIcon());

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command), typeof(ICommand), typeof(LikeButton),
        propertyChanged: (bindable, _, value) => ((LikeButton)bindable)._button.Command = (ICommand?)value);

    private readonly ImageButton _button = new();

    public LikeButton()
    {
        Content = _button;
        UpdateIcon();
    }

    public bool IsLiked
    {
        get => (bool)GetValue(IsLikedProperty);
        set => SetValue(IsLikedProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    /// <summary>Lets callers size the inner button (defaults come from the ImageButton style).</summary>
    public ImageButton Button => _button;

    private async void UpdateIcon()
    {
        _button.Source = IsLiked ? "ic_heart.png" : "ic_heart_outline.png";
        _button.Opacity = IsLiked ? 1 : 0.7;

        if (IsLiked && Handler is not null)
        {
            // Small "pop" when liking.
            await _button.ScaleToAsync(1.25, 90);
            await _button.ScaleToAsync(1, 120);
        }
    }
}
