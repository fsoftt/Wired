using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Lyrics;

namespace Wired.Presentation.ViewModels;

public sealed partial class LyricLineViewModel(LyricLine line, Action<LyricLineViewModel> seek) : ObservableObject
{
    public LyricLine Line { get; } = line;

    /// <summary>Empty LRC lines are instrumental breaks.</summary>
    public string Text => string.IsNullOrWhiteSpace(Line.Text) ? "♪" : Line.Text;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Jumps the song to this line.</summary>
    [RelayCommand]
    private void Seek() => seek(this);
}
