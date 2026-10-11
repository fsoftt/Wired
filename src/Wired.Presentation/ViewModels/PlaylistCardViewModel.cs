using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Collections;
using Wired.Core.Localization;

namespace Wired.Presentation.ViewModels;

public sealed partial class PlaylistCardViewModel(Playlist playlist, Func<Playlist, Task> open) : LocalizedObservableObject
{
    public Playlist Playlist { get; private set; } = playlist;

    /// <summary>Refreshes the card in place (an import changes its count on every track).</summary>
    public void Update(Playlist updated)
    {
        Playlist = updated;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Summary));
    }

    public string Name => Playlist.Name;

    public string Summary => L.N("Songs", Playlist.TrackIds.Count);

    [RelayCommand]
    private Task OpenAsync() => open(Playlist);
}
