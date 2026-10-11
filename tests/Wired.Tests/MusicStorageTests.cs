using Wired.Core.Models;
using Wired.Core.Storage;
using Wired.Core.UseCases;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public class MusicFileNamingTests
{
    [Theory]
    [InlineData("Mi Sueño", "Ana Guerra", "demo", "dQw4w9WgXcQ")]
    [InlineData("AC/DC: Live - Part 1 [Remix]?", "Artist - With Dash", "demo", "a-b_c-123")]
    public void Round_trips_metadata_through_the_file_name(string title, string artist, string source, string remoteId)
    {
        var fileName = MusicFileNaming.FileNameFor(title, artist, source, remoteId, ".m4a");

        Assert.True(MusicFileNaming.TryParse(fileName, out var parsed));
        Assert.Equal(source, parsed.SourceId);
        Assert.Equal(remoteId, parsed.RemoteId);
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), fileName.Contains);
        Assert.EndsWith(".m4a", fileName);
    }

    [Theory]
    [InlineData("mp4", ".m4a")]
    [InlineData(".MP4", ".m4a")]
    [InlineData(".webm", ".mka")]
    [InlineData(".mp3", ".mp3")]
    public void Uses_audio_extensions_accepted_by_shared_music_folders(string extension, string expected)
    {
        // Android refuses ".mp4"/".webm" (video types) inside Music/.
        Assert.EndsWith(expected, MusicFileNaming.FileNameFor("T", "A", "demo", "id", extension));
    }

    [Fact]
    public void Ignores_files_it_did_not_name()
    {
        Assert.False(MusicFileNaming.TryParse("random song.mp3", out _));
    }
}

public sealed class MusicStorageUseCasesTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly InMemoryLibrary _library = new();

    [Fact]
    public async Task Recovers_named_files_missing_from_the_library()
    {
        Directory.CreateDirectory(_paths.MusicDirectory);
        await File.WriteAllTextAsync(Path.Combine(_paths.MusicDirectory, "Ana Guerra - Mi Sueño [demo-abc].m4a"), "a");
        await File.WriteAllTextAsync(Path.Combine(_paths.MusicDirectory, "Known - Song [demo-known].m4a"), "b");
        await File.WriteAllTextAsync(Path.Combine(_paths.MusicDirectory, "cover.jpg"), "c");
        _library.Tracks.Add(TestData.LocalTrack("Song") with { SourceId = "demo", RemoteId = "known" });

        var recovered = await new RecoverMusicUseCase(_library, _paths).ExecuteAsync();

        Assert.Equal(1, recovered);
        var track = _library.Tracks.Single(t => t.RemoteId == "abc");
        Assert.Equal(("Ana Guerra", "Mi Sueño", "demo"), (track.Artist, track.Title, track.SourceId));
    }

    [Fact]
    public async Task Moves_old_private_downloads_into_the_music_folder()
    {
        var privateDir = Path.Combine(_paths.DataDirectory, "music", "demo");
        Directory.CreateDirectory(privateDir);
        var oldPath = Path.Combine(privateDir, "abc.m4a");
        await File.WriteAllTextAsync(oldPath, "audio");
        var track = new LocalTrack(Guid.NewGuid(), "Mi Sueño", "Ana Guerra", null, null, oldPath, "demo", "abc", DateTimeOffset.Now);
        _library.Tracks.Add(track);

        var moved = await new MoveMusicToSharedFolderUseCase(_library, _paths).ExecuteAsync();

        Assert.Equal(1, moved);
        var updated = _library.Tracks.Single();
        Assert.Equal(Path.Combine(_paths.MusicDirectory, "Ana Guerra - Mi Sueño [demo-abc].m4a"), updated.FilePath);
        Assert.True(File.Exists(updated.FilePath));
        Assert.False(File.Exists(oldPath));

        // Running again does nothing.
        Assert.Equal(0, await new MoveMusicToSharedFolderUseCase(_library, _paths).ExecuteAsync());
    }

    [Fact]
    public async Task Migration_renames_video_extensions_to_audio()
    {
        var privateDir = Path.Combine(_paths.DataDirectory, "music");
        Directory.CreateDirectory(privateDir);
        var oldPath = Path.Combine(privateDir, "abc.mp4");
        await File.WriteAllTextAsync(oldPath, "audio");
        _library.Tracks.Add(new LocalTrack(Guid.NewGuid(), "T", "A", null, null, oldPath, "demo", "abc", DateTimeOffset.Now));

        await new MoveMusicToSharedFolderUseCase(_library, _paths).ExecuteAsync();

        Assert.EndsWith("A - T [demo-abc].m4a", _library.Tracks.Single().FilePath);
    }

    public void Dispose() => _paths.Dispose();
}
