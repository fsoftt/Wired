using Wired.Core.Browsing;
using Wired.Core.Collections;
using Wired.Core.Models;
using Wired.Infrastructure.Collections;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class MediaBrowseTreeTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly InMemoryLibrary _library = new();
    private readonly PlaylistService _playlists;
    private readonly LikesService _likes;

    public MediaBrowseTreeTests()
    {
        _playlists = new PlaylistService(new JsonPlaylistRepository(_paths), _library, new FixedClock(DateTimeOffset.UnixEpoch));
        _likes = new LikesService(new JsonLikedTracksRepository(_paths), _library);
    }

    private MediaBrowseTree Tree() => new(_library, _playlists, _likes);

    private static LocalTrack Track(string title, string artist) => TestData.LocalTrack(title) with { Artist = artist };

    [Fact]
    public async Task Root_has_library_likes_and_playlists_folders_with_counts()
    {
        var a = Track("Unstoppable", "Sia");
        var b = Track("Cheap Thrills", "Sia");
        _library.Tracks.AddRange([a, b]);
        await _likes.ToggleAsync(a);
        await _playlists.CreateAsync("Correr");
        await _playlists.CreateAsync("Dormir");

        var root = await Tree().GetChildrenAsync(MediaBrowseTree.RootId);

        Assert.Equal<string>(["library", "likes", "playlists"], root.Select(n => n.Id));
        Assert.All(root, n => Assert.False(n.IsPlayable));
        Assert.Equal<string>(["2 canciones", "1 canción", "2 listas"], root.Select(n => n.Subtitle!));
    }

    [Fact]
    public async Task Library_starts_with_shuffle_then_one_playable_node_per_track()
    {
        var a = Track("Uno", "Artista A");
        var b = Track("Dos", "Artista B");
        _library.Tracks.AddRange([a, b]);

        var children = await Tree().GetChildrenAsync("library");

        Assert.Equal<string>(["shuffle|library", $"track|library|{a.Id:N}", $"track|library|{b.Id:N}"], children.Select(n => n.Id));
        Assert.Equal<string>(["Aleatorio", "Uno", "Dos"], children.Select(n => n.Title));
        Assert.Equal<string>(["Artista A", "Artista B"], children.Skip(1).Select(n => n.Subtitle!));
        Assert.All(children, n => Assert.True(n.IsPlayable));
    }

    [Fact]
    public async Task Single_track_list_has_no_shuffle_node()
    {
        var a = Track("Solo", "Artista");
        _library.Tracks.Add(a);
        await _likes.ToggleAsync(a);

        var library = await Tree().GetChildrenAsync("library");
        var likes = await Tree().GetChildrenAsync("likes");

        Assert.Equal<string>([$"track|library|{a.Id:N}"], library.Select(n => n.Id));
        Assert.Equal<string>([$"track|likes|{a.Id:N}"], likes.Select(n => n.Id));
    }

    [Fact]
    public async Task Playlists_are_browsable_and_open_in_playlist_order()
    {
        var a = Track("A", "X");
        var b = Track("B", "Y");
        var c = Track("C", "Z");
        _library.Tracks.AddRange([a, b, c]);
        var playlist = await _playlists.CreateAsync("Para correr");
        playlist = await _playlists.AddTrackAsync(playlist, c);
        await _playlists.AddTrackAsync(playlist, a);

        var lists = await Tree().GetChildrenAsync("playlists");
        var folder = Assert.Single(lists);

        Assert.Equal($"playlist|{playlist.Id:N}", folder.Id);
        Assert.Equal("Para correr", folder.Title);
        Assert.False(folder.IsPlayable);

        var tracks = await Tree().GetChildrenAsync(folder.Id);
        Assert.Equal<string>(["shuffle|playlist:" + playlist.Id.ToString("N"), $"track|playlist:{playlist.Id:N}|{c.Id:N}", $"track|playlist:{playlist.Id:N}|{a.Id:N}"], tracks.Select(n => n.Id));
        Assert.Equal<string>(["Aleatorio", "C", "A"], tracks.Select(n => n.Title));
    }

    [Fact]
    public async Task Likes_folder_lists_liked_tracks_newest_first()
    {
        var a = Track("A", "Artista A");
        var b = Track("B", "Artista B");
        _library.Tracks.AddRange([a, b]);
        await _likes.ToggleAsync(a);
        await _likes.ToggleAsync(b);

        var children = await Tree().GetChildrenAsync("likes");

        Assert.Equal<string>(["shuffle|likes", $"track|likes|{b.Id:N}", $"track|likes|{a.Id:N}"], children.Select(n => n.Id));
    }

    [Fact]
    public async Task Resolving_a_track_returns_its_whole_context_and_start_index()
    {
        var a = Track("A", "X");
        var b = Track("B", "X");
        var c = Track("C", "X");
        _library.Tracks.AddRange([a, b, c]);
        var playlist = await _playlists.CreateAsync("Lista");
        playlist = await _playlists.AddTrackAsync(playlist, c);
        await _playlists.AddTrackAsync(playlist, a);

        var fromLibrary = await Tree().ResolveAsync($"track|library|{b.Id:N}");
        Assert.NotNull(fromLibrary);
        Assert.Equal([a.Id, b.Id, c.Id], fromLibrary.Value.Items.Select(i => i.Id));
        Assert.Equal(1, fromLibrary.Value.StartIndex);
        Assert.False(fromLibrary.Value.Shuffle);

        var fromPlaylist = await Tree().ResolveAsync($"track|playlist:{playlist.Id:N}|{a.Id:N}");
        Assert.NotNull(fromPlaylist);
        Assert.Equal([c.Id, a.Id], fromPlaylist.Value.Items.Select(i => i.Id));
        Assert.Equal(1, fromPlaylist.Value.StartIndex);
        Assert.False(fromPlaylist.Value.Shuffle);
    }

    [Fact]
    public async Task Resolving_the_shuffle_node_returns_the_whole_list_shuffled()
    {
        var a = Track("A", "X");
        var b = Track("B", "X");
        _library.Tracks.AddRange([a, b]);

        var resolved = await Tree().ResolveAsync("shuffle|library");

        Assert.NotNull(resolved);
        Assert.Equal([a.Id, b.Id], resolved.Value.Items.Select(i => i.Id));
        Assert.Equal(0, resolved.Value.StartIndex);
        Assert.True(resolved.Value.Shuffle);
    }

    [Fact]
    public async Task Unknown_or_malformed_ids_resolve_to_null_and_unknown_folders_are_empty()
    {
        var a = Track("A", "X");
        _library.Tracks.Add(a);
        var tree = Tree();

        Assert.Null(await tree.ResolveAsync("track|library|not-a-guid"));
        Assert.Null(await tree.ResolveAsync("track|library|"));
        Assert.Null(await tree.ResolveAsync("track|library"));
        Assert.Null(await tree.ResolveAsync("track|library|x|y"));
        Assert.Null(await tree.ResolveAsync($"track|library|{Guid.NewGuid():N}"));
        Assert.Null(await tree.ResolveAsync($"track|playlist:{Guid.NewGuid():N}|{a.Id:N}"));
        Assert.Null(await tree.ResolveAsync("shuffle|likes"));
        Assert.Null(await tree.ResolveAsync("bogus"));
        Assert.Null(await tree.ResolveAsync(""));

        Assert.Empty(await tree.GetChildrenAsync("nope"));
        Assert.Empty(await tree.GetChildrenAsync($"playlist|{Guid.NewGuid():N}"));
        Assert.Empty(await tree.GetChildrenAsync($"track|library|{a.Id:N}"));
    }

    [Fact]
    public async Task Search_matches_all_words_ignoring_case_and_accents()
    {
        _library.Tracks.AddRange(
        [
            Track("Unstoppable", "Sia"),
            Track("Cheap Thrills", "Sia"),
            Track("Unstoppable", "Other Artist"),
            Track("Chandelier", "Sía"),
        ]);
        var tree = Tree();

        var matches = await tree.SearchAsync("sia unstop");
        Assert.Equal<string>(["Unstoppable"], matches.Select(i => i.Title));
        Assert.Equal<string>(["Sia"], matches.Select(i => i.Artist));

        Assert.Equal<string>(["Unstoppable", "Cheap Thrills", "Chandelier"], (await tree.SearchAsync("SÍA")).Select(i => i.Title));
        Assert.Equal(4, (await tree.SearchAsync("")).Count);
        Assert.Equal(4, (await tree.SearchAsync(null)).Count);
    }

    public void Dispose() => _paths.Dispose();
}
