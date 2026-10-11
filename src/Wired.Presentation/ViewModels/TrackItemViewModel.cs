using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Presentation.Formatting;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>
/// A downloaded track in any list (library, playlist, likes). Its commands delegate to the owning
/// <see cref="TrackListViewModel"/> so the view can bind them directly (compiled bindings, no relative sources).
/// </summary>
public sealed partial class TrackItemViewModel(LocalTrack track, TrackListViewModel owner, TrackCollectionActions actions)
    : ObservableObject
{
    public LocalTrack Track { get; private set; } = track;

    /// <summary>
    /// Shows a new version of the same track (new cover or names) in place: replacing the row
    /// instead would make the list jump while the user scrolls.
    /// </summary>
    public void Update(LocalTrack updated)
    {
        Track = updated;
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Artist));
        OnPropertyChanged(nameof(ThumbnailUrl));
        OnPropertyChanged(nameof(DurationText));
    }

    public string Title => Track.Title;

    public string Artist => Track.Artist;

    /// <summary>Cover saved on the device when available, else the remote thumbnail.</summary>
    public string? ThumbnailUrl => Track.Artwork;

    public string DurationText => TimeFormatter.Format(Track.Duration);

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsLiked { get; set; }

    [RelayCommand]
    private Task PlayAsync() => owner.PlayAsync(this);

    [RelayCommand]
    private Task RemoveAsync() => owner.RemoveAsync(this);

    [RelayCommand]
    private Task ToggleLikeAsync() => actions.ToggleLikeAsync(Track);

    [RelayCommand]
    private Task AddToPlaylistAsync() => actions.AddToPlaylistAsync(Track);

    /// <summary>Swipe action.</summary>
    [RelayCommand]
    private Task AddToQueueAsync() => actions.Queue.AddToQueueAsync(PlaybackItem.FromLocal(Track));

    /// <summary>Long press.</summary>
    [RelayCommand]
    private Task ShowMenuAsync() =>
        actions.ShowMenuAsync(Track, new MenuOption(owner.RemoveLabel, () => owner.RemoveAsync(this)));

    public bool Matches(string filter) =>
        Title.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
        Artist.Contains(filter, StringComparison.CurrentCultureIgnoreCase);
}
