using Wired.Core.Collections;
using Wired.Core.Localization;
using Wired.Core.Models;
using Wired.Core.Playback;

namespace Wired.Presentation.Services;

/// <summary>
/// User flows shared by every screen that shows tracks or playlists:
/// liking, adding to a playlist, creating and renaming playlists.
/// </summary>
public sealed class TrackCollectionActions(
    LikesService likes,
    PlaylistService playlists,
    QueueActions queue,
    IDialogService dialogs)
{
    public QueueActions Queue => queue;

    /// <summary>Long-press menu of a downloaded track; <paramref name="contextual"/> adds screen-specific entries.</summary>
    public Task ShowMenuAsync(LocalTrack track, params MenuOption[] contextual)
    {
        var item = PlaybackItem.FromLocal(track);
        return ShowMenuAsync(
            track.Title,
            [
                new MenuOption(L.T("Menu_PlayNext"), () => queue.PlayNextAsync(item)),
                new MenuOption(L.T("Menu_AddToQueue"), () => queue.AddToQueueAsync(item)),
                new MenuOption(L.T(likes.IsLiked(track.Id) ? "Menu_Unlike" : "Menu_Like"), () => ToggleLikeAsync(track)),
                new MenuOption(L.T("Menu_AddToPlaylist"), () => AddToPlaylistAsync(track)),
                .. contextual,
            ]);
    }

    public async Task ShowMenuAsync(string title, IReadOnlyList<MenuOption> options)
    {
        var choice = await dialogs.ChooseAsync(title, L.T("Common_Cancel"), options.Select(o => o.Label).ToList());
        if (choice is { } index)
            await options[index].Run();
    }

    public Task<bool> ToggleLikeAsync(LocalTrack track) => likes.ToggleAsync(track);

    public async Task AddToPlaylistAsync(LocalTrack track)
    {
        var existing = await playlists.GetAllAsync();
        var options = existing.Select(p => p.Name).Prepend(L.T("Playlist_NewOption")).ToList();

        var choice = await dialogs.ChooseAsync(L.T("Menu_AddToPlaylist"), L.T("Common_Cancel"), options);
        if (choice is not { } index)
            return;

        var target = index == 0 ? await CreatePlaylistAsync() : existing[index - 1];
        if (target is null)
            return;

        if (target.Contains(track.Id))
        {
            await dialogs.ShowToastAsync(L.T("Playlist_AlreadyIn", target.Name));
            return;
        }

        await playlists.AddTrackAsync(target, track);
        await dialogs.ShowToastAsync(L.T("Playlist_AddedTo", target.Name));
    }

    /// <returns>The new playlist, or null when the user canceled.</returns>
    public async Task<Playlist?> CreatePlaylistAsync()
    {
        var name = await PromptNameAsync(L.T("Playlist_NewTitle"), L.T("Playlist_NewMessage"), L.T("Common_Create"), initialValue: null);
        return name is null ? null : await playlists.CreateAsync(name);
    }

    public async Task RenamePlaylistAsync(Playlist playlist)
    {
        var name = await PromptNameAsync(L.T("Playlist_RenameTitle"), L.T("Playlist_RenameMessage"), L.T("Common_Save"), playlist.Name);
        if (name is not null && name != playlist.Name)
            await playlists.RenameAsync(playlist, name);
    }

    /// <summary>Asks for a playlist name until it is valid or the user cancels.</summary>
    private async Task<string?> PromptNameAsync(string title, string message, string accept, string? initialValue)
    {
        while (true)
        {
            var name = await dialogs.PromptAsync(title, message, accept, L.T("Common_Cancel"), initialValue, Playlist.MaxNameLength);
            if (name is null)
                return null;

            try
            {
                return PlaylistService.NormalizeName(name);
            }
            catch (ArgumentException error)
            {
                message = error.Message;
                initialValue = name;
            }
        }
    }
}
