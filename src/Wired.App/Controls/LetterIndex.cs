namespace Wired.App.Controls;

/// <summary>
/// Vertical strip of letters for fast scrolling: tap a letter, or slide along the strip.
/// Each letter knows its own place, so a slide started on it can be followed with the
/// pan's relative movement.
/// </summary>
public sealed class LetterIndex : ContentView
{
    public static readonly BindableProperty LettersProperty = BindableProperty.Create(
        nameof(Letters), typeof(IReadOnlyList<string>), typeof(LetterIndex),
        propertyChanged: (bindable, _, _) => ((LetterIndex)bindable).Build());

    private readonly Grid _grid = new() { RowSpacing = 0 };
    private readonly List<Label> _labels = [];
    private int _panStart;
    private string? _current;

    public LetterIndex()
    {
        Content = _grid;
        Padding = new Thickness(2, 4);
    }

    /// <summary>Raised with the letter under the finger, once per change.</summary>
    public event EventHandler<string>? LetterSelected;

    public IReadOnlyList<string>? Letters
    {
        get => (IReadOnlyList<string>?)GetValue(LettersProperty);
        set => SetValue(LettersProperty, value);
    }

    private void Build()
    {
        _grid.Children.Clear();
        _grid.RowDefinitions.Clear();
        _labels.Clear();

        var letters = Letters ?? [];
        for (var i = 0; i < letters.Count; i++)
        {
            var index = i;
            var label = new Label
            {
                Text = letters[i],
                FontSize = 11,
                FontFamily = "OpenSansSemibold",
                TextColor = Color.FromArgb("#A78BFA"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => Select(index, force: true);
            label.GestureRecognizers.Add(tap);

            var pan = new PanGestureRecognizer();
            pan.PanUpdated += (_, e) => OnPan(index, e);
            label.GestureRecognizers.Add(pan);

            _grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            _grid.Add(label, 0, i);
            _labels.Add(label);
        }
    }

    private void OnPan(int startIndex, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStart = startIndex;
                Select(startIndex, force: true);
                break;
            case GestureStatus.Running when _labels.Count > 0:
                var rowHeight = Math.Max(1, _labels[0].Height);
                Select(_panStart + (int)Math.Round(e.TotalY / rowHeight), force: false);
                break;
            default:
                _current = null;
                break;
        }
    }

    private void Select(int index, bool force)
    {
        if (_labels.Count == 0)
            return;

        var letter = _labels[Math.Clamp(index, 0, _labels.Count - 1)].Text;
        if (!force && letter == _current)
            return;

        _current = letter;
        LetterSelected?.Invoke(this, letter);
    }
}
