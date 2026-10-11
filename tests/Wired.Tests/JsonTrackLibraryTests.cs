using Wired.Infrastructure.Library;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class JsonTrackLibraryTests : IDisposable
{
    private readonly TempPaths _paths = new();

    [Fact]
    public async Task Persists_tracks_between_instances()
    {
        var track = await CreateTrackOnDiskAsync("song");
        await new JsonTrackLibrary(_paths).AddAsync(track);

        var reloaded = await new JsonTrackLibrary(_paths).GetAllAsync();

        Assert.Equal([track], reloaded);
    }

    [Fact]
    public async Task Hides_unreachable_files_without_forgetting_them()
    {
        // After a reinstall, shared files are unreadable until permission is granted again.
        var track = await CreateTrackOnDiskAsync("song");
        await new JsonTrackLibrary(_paths).AddAsync(track);
        var bytes = await File.ReadAllBytesAsync(track.FilePath);
        File.Delete(track.FilePath);

        var library = new JsonTrackLibrary(_paths);
        Assert.Empty(await library.GetAllAsync());
        Assert.Equal([track], await library.GetAllStoredAsync());

        await File.WriteAllBytesAsync(track.FilePath, bytes);
        Assert.Equal([track], await library.GetAllAsync());
    }

    [Fact]
    public async Task Remove_deletes_the_entry()
    {
        var library = new JsonTrackLibrary(_paths);
        var track = await CreateTrackOnDiskAsync("song");
        await library.AddAsync(track);

        await library.RemoveAsync(track.Id);

        Assert.Null(await library.FindAsync(track.SourceId, track.RemoteId));
    }

    private async Task<Core.Models.LocalTrack> CreateTrackOnDiskAsync(string name)
    {
        Directory.CreateDirectory(_paths.MusicDirectory);
        var path = Path.Combine(_paths.MusicDirectory, $"{name}.m4a");
        await File.WriteAllTextAsync(path, "audio");
        return TestData.LocalTrack(name, path);
    }

    public void Dispose() => _paths.Dispose();
}
