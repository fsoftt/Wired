namespace Wired.App.Controls;

/// <summary>
/// Rounded square cover art with a branded placeholder.
/// </summary>
public partial class Artwork : ContentView
{
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(string), typeof(Artwork),
        propertyChanged: (bindable, _, value) => ((Artwork)bindable).OnSourceChanged(value as string));

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(double), typeof(Artwork), 12d,
        propertyChanged: (bindable, _, value) => ((Artwork)bindable).OnCornerRadiusChanged((double)value));

    public Artwork()
    {
        InitializeComponent();
        OnCornerRadiusChanged(CornerRadius);
    }

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        Placeholder.WidthRequest = Placeholder.HeightRequest = Math.Max(16, Math.Min(width, height) * 0.45);
    }

    /// <summary>A cover saved on the device (file path) or a remote thumbnail (URL).</summary>
    private void OnSourceChanged(string? source) =>
        Thumbnail.Source = source switch
        {
            null or "" => null,
            _ when File.Exists(source) => ImageSource.FromFile(source),
            _ when Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                => new UriImageSource { Uri = uri, CacheValidity = TimeSpan.FromDays(7) },
            _ => null,
        };

    private void OnCornerRadiusChanged(double radius) =>
        Card.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = radius };
}
