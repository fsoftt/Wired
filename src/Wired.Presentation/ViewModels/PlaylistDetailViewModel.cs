using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Collections;
using Wired.Core.Localization;
using Wired.Core.Playback;
using Wired.Presentation.Collections;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>
/// Shows a playlist or, when opened without a playlist id, the "Me gusta" collection.
/// </summary>
public sealed partial class PlaylistDetailViewModel : TrackListViewModel
{
    private readonly PlaylistService _playlists;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly IUiDispatcher _dispatcher;

    private Guid? _playlistId;
    private Playlist? _playlist;

    public PlaylistDetailViewModel(
        PlaylistService playlists,
        IPlaybackService playback,
        LikesService likes,
        TrackCollectionActions actions,
        INavigationService navigation,
        IDialogService dialogs,
        IUiDispatcher dispatcher)
        : base(playback, likes, actions, dispatcher)
    {
        _playlists = playlists;
        _navigation = navigation;
        _dialogs = dialogs;
        _dispatcher = dispatcher;
        Title = string.Empty;
    }

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlaylist))]
    public partial bool IsLikedCollection { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(IsEmpty), nameof(HasTracks))]
    public partial int TrackCount { get; private set; }

    public bool IsPlaylist => !IsLikedCollection;

    public string Summary => L.N("Songs", TrackCount);

    public bool IsEmpty => TrackCount == 0;

    public bool HasTracks => TrackCount > 0;

    public string EmptyMessage => IsLikedCollection
        ? L.T("Detail_EmptyLiked")
        : L.T("Detail_EmptyPlaylist");

    /// <param name="playlistId">The playlist to show, or null for liked tracks.</param>
    public Task OpenAsync(Guid? playlistId)
    {
        _playlistId = playlistId;
        IsLikedCollection = playlistId is null;
        OnPropertyChanged(nameof(EmptyMessage));
        return ReloadAsync();
    }

    /// <summary>Call when the screen is shown: listens to changes and refreshes.</summary>
    public Task AppearAsync()
    {
        Activate();
        _playlists.PlaylistsChanged += OnPlaylistsChanged;
        return ReloadAsync();
    }

    public void Disappear()
    {
        Deactivate();
        _playlists.PlaylistsChanged -= OnPlaylistsChanged;
    }

    public override string RemoveLabel => L.T(IsLikedCollection ? "Menu_Unlike" : "Detail_RemoveFromPlaylist");

    public override Task RemoveAsync(TrackItemViewModel item) =>
        _playlist is null
            ? Likes.SetLikedAsync(item.Track.Id, liked: false)
            : _playlists.RemoveTrackAsync(_playlist, item.Track.Id);

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (_playlist is not null)
            await Actions.RenamePlaylistAsync(_playlist);
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (_playlist is null)
            return;

        var confirmed = await _dialogs.ConfirmAsync(
            L.T("Detail_DeleteTitle"),
            L.T("Detail_DeleteMessage", _playlist.Name),
            L.T("Common_Delete"),
            L.T("Common_Cancel"));

        if (!confirmed)
            return;

        await _playlists.DeleteAsync(_playlist);
        await _navigation.GoBackAsync();
    }

    protected override void OnLikeChanged(LikeChangedEventArgs e)
    {
        base.OnLikeChanged(e);
        if (IsLikedCollection)
            _ = ReloadAsync();
    }

    private void OnPlaylistsChanged(object? sender, EventArgs e) => _dispatcher.Post(() => _ = ReloadAsync());

    private async Task ReloadAsync()
    {
        await Likes.InitializeAsync();

        if (IsLikedCollection)
        {
            Title = L.T("Likes_Title");
            Fill(await Likes.GetLikedTracksAsync());
            return;
        }

        _playlist = _playlistId is { } id ? await _playlists.FindAsync(id) : null;
        if (_playlist is null)
        {
            // Deleted elsewhere.
            Fill([]);
            return;
        }

        Title = _playlist.Name;
        Fill(await _playlists.GetTracksAsync(_playlist));
    }

    /// <summary>
    /// Reuses the rows of tracks already shown (an import fills the playlist one track at a time):
    /// rebuilding would make the list jump.
    /// </summary>
    private void Fill(IReadOnlyList<Core.Models.LocalTrack> tracks)
    {
        var existing = Tracks.GroupBy(t => t.Track.Id).ToDictionary(g => g.Key, g => new Queue<TrackItemViewModel>(g));
        var target = tracks
            .Select(track =>
            {
                if (existing.TryGetValue(track.Id, out var rows) && rows.TryDequeue(out var row))
                {
                    row.Update(track);
                    return row;
                }

                return CreateItem(track);
            })
            .ToList();

        Tracks.SyncTo(target);
        TrackCount = Tracks.Count;
    }
}
