using Wired.Core.Collections;
using Wired.Infrastructure.Collections;
using Wired.Infrastructure.Library;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class CollectionsTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly InMemoryLibrary _library = new();

    [Fact]
    public async Task Playlists_persist_and_keep_track_order()
    {
        var a = TestData.LocalTrack("a");
        var b = TestData.LocalTrack("b");
        _library.Tracks.AddRange([a, b]);
        var service = CreatePlaylists();

        var playlist = await service.CreateAsync("  Para correr  ");
        playlist = await service.AddTrackAsync(playlist, b);
        playlist = await service.AddTrackAsync(playlist, a);
        playlist = await service.AddTrackAsync(playlist, a); // Duplicates are ignored.

        var reloaded = (await CreatePlaylists().GetAllAsync()).Single();
        Assert.Equal("Para correr", reloaded.Name);
        Assert.Equal([b, a], await service.GetTracksAsync(reloaded));
    }

    [Fact]
    public async Task Invalid_playlist_names_are_rejected()
    {
        var service = CreatePlaylists();

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync("   "));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new string('x', Playlist.MaxNameLength + 1)));
    }

    [Fact]
    public async Task Removing_a_track_everywhere_updates_every_playlist()
    {
        var a = TestData.LocalTrack("a");
        _library.Tracks.Add(a);
        var service = CreatePlaylists();
        var first = await service.AddTrackAsync(await service.CreateAsync("Uno"), a);
        await service.AddTrackAsync(await service.CreateAsync("Dos"), a);

        await service.RemoveTrackFromAllAsync(a.Id);

        Assert.All(await service.GetAllAsync(), p => Assert.Empty(p.TrackIds));
        Assert.NotEmpty(first.TrackIds); // Records are immutable snapshots.
    }

    [Fact]
    public async Task Likes_toggle_persist_and_list_newest_first()
    {
        var a = TestData.LocalTrack("a");
        var b = TestData.LocalTrack("b");
        _library.Tracks.AddRange([a, b]);
        var likes = CreateLikes();
        LikeChangedEventArgs? lastChange = null;
        likes.LikeChanged += (_, e) => lastChange = e;

        Assert.True(await likes.ToggleAsync(a));
        Assert.True(await likes.ToggleAsync(b));
        Assert.Equal(b.Id, lastChange?.TrackId);

        var reloaded = CreateLikes();
        await reloaded.InitializeAsync();
        Assert.True(reloaded.IsLiked(a.Id));
        Assert.Equal([b, a], await reloaded.GetLikedTracksAsync());

        Assert.False(await reloaded.ToggleAsync(a));
        Assert.Equal([b], await reloaded.GetLikedTracksAsync());
    }

    [Fact]
    public async Task Library_round_trips_with_reflection_based_json_disabled()
    {
        // Regression: trimmed Release builds (the Android APK) disable reflection-based JSON,
        // which made every download fail when saving the library.
        Directory.CreateDirectory(_paths.MusicDirectory);
        var path = Path.Combine(_paths.MusicDirectory, "song.m4a");
        await File.WriteAllTextAsync(path, "audio");
        var track = TestData.LocalTrack("song", path);

        await new JsonTrackLibrary(_paths).AddAsync(track);

        Assert.Equal([track], await new JsonTrackLibrary(_paths).GetAllAsync());
    }

    private PlaylistService CreatePlaylists() =>
        new(new JsonPlaylistRepository(_paths), _library, new FixedClock(DateTimeOffset.UnixEpoch));

    private LikesService CreateLikes() => new(new JsonLikedTracksRepository(_paths), _library);

    public void Dispose() => _paths.Dispose();
}
