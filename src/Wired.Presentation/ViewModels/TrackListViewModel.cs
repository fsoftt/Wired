using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Collections;
using Wired.Core.Models;
using Wired.Core.Playback;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>
/// Behavior shared by every screen listing downloaded tracks: play, shuffle,
/// highlight the playing track and keep likes in sync.
/// </summary>
public abstract partial class TrackListViewModel : ObservableObject
{
    private readonly IUiDispatcher _dispatcher;
    private bool _isActive;

    protected TrackListViewModel(
        IPlaybackService playback,
        LikesService likes,
        TrackCollectionActions actions,
        IUiDispatcher dispatcher)
    {
        Playback = playback;
        Likes = likes;
        Actions = actions;
        _dispatcher = dispatcher;
    }

    public ObservableCollection<TrackItemViewModel> Tracks { get; } = [];

    protected IPlaybackService Playback { get; }

    protected LikesService Likes { get; }

    protected TrackCollectionActions Actions { get; }

    /// <summary>Every item owned by the list, including the ones hidden by a filter.</summary>
    protected virtual IEnumerable<TrackItemViewModel> AllItems => Tracks;

    /// <summary>Starts listening to playback and like changes. Idempotent.</summary>
    public void Activate()
    {
        if (_isActive)
            return;

        _isActive = true;
        Playback.StateChanged += OnPlaybackStateChanged;
        Likes.LikeChanged += OnLikeChangedHandler;
        HighlightCurrentTrack();
    }

    public void Deactivate()
    {
        if (!_isActive)
            return;

        _isActive = false;
        Playback.StateChanged -= OnPlaybackStateChanged;
        Likes.LikeChanged -= OnLikeChangedHandler;
    }

    public Task PlayAsync(TrackItemViewModel item) =>
        Playback.PlayAsync(Tracks.Select(t => t.Track).ToList(), Tracks.IndexOf(item));

    public abstract Task RemoveAsync(TrackItemViewModel item);

    /// <summary>Menu label of <see cref="RemoveAsync"/> on this screen.</summary>
    public abstract string RemoveLabel { get; }

    [RelayCommand]
    private Task PlayAllAsync() =>
        Tracks.Count == 0 ? Task.CompletedTask : Playback.PlayAsync(Tracks.Select(t => t.Track).ToList());

    [RelayCommand]
    private Task ShuffleAsync() =>
        Tracks.Count == 0 ? Task.CompletedTask : Playback.ShuffleAsync(Tracks.Select(t => t.Track).ToList());

    protected TrackItemViewModel CreateItem(LocalTrack track) => new(track, this, Actions)
    {
        IsLiked = Likes.IsLiked(track.Id),
        IsCurrent = Playback.CurrentItem?.Id == track.Id,
    };

    protected void HighlightCurrentTrack()
    {
        var currentId = Playback.CurrentItem?.Id;
        foreach (var item in AllItems)
            item.IsCurrent = item.Track.Id == currentId;
    }

    protected virtual void OnLikeChanged(LikeChangedEventArgs e)
    {
        foreach (var item in AllItems.Where(i => i.Track.Id == e.TrackId))
            item.IsLiked = e.IsLiked;
    }

    private void OnPlaybackStateChanged(object? sender, EventArgs e) => _dispatcher.Post(HighlightCurrentTrack);

    private void OnLikeChangedHandler(object? sender, LikeChangedEventArgs e) => _dispatcher.Post(() => OnLikeChanged(e));
}
