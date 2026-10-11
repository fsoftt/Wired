using Wired.Core.Collections;
using Wired.Core.Models;
using Wired.Core.UseCases;
using Wired.Infrastructure.Collections;
using Wired.Infrastructure.Library;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class DuplicateTracksTests : IDisposable
{
    private readonly TempPaths _paths = new();

    [Fact]
    public async Task Adding_the_same_song_twice_keeps_the_first_copy()
    {
        var library = new JsonTrackLibrary(_paths);
        var first = TestData.LocalTrack("song");
        var second = first with { Id = Guid.NewGuid() };

        var stored = await Task.WhenAll(library.AddAsync(first), library.AddAsync(second));

        Assert.Equal(first, stored[0]);
        Assert.Equal(first, stored[1]);
        Assert.Single(await library.GetAllStoredAsync());
    }

    [Fact]
    public async Task Duplicates_already_stored_are_merged_into_one_track_with_their_playlists_and_likes()
    {
        var kept = TestData.LocalTrack("song") with { IsEnriched = true, AddedAt = DateTimeOffset.Now.AddDays(-1) };
        var copy = kept with { Id = Guid.NewGuid(), IsEnriched = false, AddedAt = DateTimeOffset.Now };
        var other = TestData.LocalTrack("other");
        await WriteLibraryAsync(copy, kept, other);

        var library = new JsonTrackLibrary(_paths);
        var playlists = new PlaylistService(new JsonPlaylistRepository(_paths), library, new FixedClock(DateTimeOffset.Now));
        var likes = new LikesService(new JsonLikedTracksRepository(_paths), library);
        var playlist = await playlists.CreateAsync("Mix");
        await playlists.SetTracksAsync(playlist, [copy.Id, other.Id, kept.Id]);
        await likes.SetLikedAsync(copy.Id, true);

        var removed = await new RemoveDuplicateTracksUseCase(library, playlists, likes).ExecuteAsync();

        Assert.Equal(1, removed);
        Assert.Equal([kept.Id, other.Id], (await library.GetAllStoredAsync()).Select(t => t.Id));
        Assert.Equal([kept.Id, other.Id], (await playlists.FindAsync(playlist.Id))!.TrackIds);
        Assert.True(likes.IsLiked(kept.Id));
        Assert.False(likes.IsLiked(copy.Id));
    }

    [Fact]
    public async Task Runs_only_once()
    {
        var library = new InMemoryLibrary();
        var useCase = new RemoveDuplicateTracksUseCase(
            library,
            new PlaylistService(new JsonPlaylistRepository(_paths), library, new FixedClock(DateTimeOffset.Now)),
            new LikesService(new JsonLikedTracksRepository(_paths), library));

        Assert.Same(useCase.ExecuteAsync(), useCase.ExecuteAsync());
        Assert.Equal(0, await useCase.ExecuteAsync());
    }

    /// <summary>Libraries written by older versions could hold the same song twice.</summary>
    private async Task WriteLibraryAsync(params LocalTrack[] tracks)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var entries = tracks.Select(t => $$"""
            {"Id":"{{t.Id}}","Title":"{{t.Title}}","Artist":"{{t.Artist}}","Duration":null,"ThumbnailUrl":null,
             "FilePath":"{{t.FilePath}}","SourceId":"{{t.SourceId}}","RemoteId":"{{t.RemoteId}}",
             "AddedAt":"{{t.AddedAt:O}}","IsEnriched":{{(t.IsEnriched ? "true" : "false")}}}
            """);
        await File.WriteAllTextAsync(Path.Combine(_paths.DataDirectory, "library.json"), $"[{string.Join(",", entries)}]");
    }

    public void Dispose() => _paths.Dispose();
}
