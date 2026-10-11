using Wired.Core.Plugins;
using Wired.Core.UseCases;
using Wired.Tests.Fakes;

namespace Wired.Tests;

public sealed class DownloadTrackUseCaseTests : IDisposable
{
    private readonly TempPaths _paths = new();
    private readonly FakePlugin _plugin = new();
    private readonly InMemoryLibrary _library = new();
    private readonly DownloadTrackUseCase _useCase;

    public DownloadTrackUseCaseTests() =>
        _useCase = new DownloadTrackUseCase(new PluginCatalog([_plugin]), _library, _paths, new FixedClock(DateTimeOffset.UnixEpoch));

    [Fact]
    public async Task Downloads_into_plugin_folder_and_adds_to_library()
    {
        var remote = TestData.RemoteTrack("xyz");

        var local = await _useCase.ExecuteAsync(remote);

        Assert.True(File.Exists(local.FilePath));
        Assert.Equal(Path.Combine(_paths.MusicDirectory, "Artist - Title xyz [fake-xyz].m4a"), local.FilePath);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_paths.CacheDirectory, "downloads")));
        Assert.Equal(remote.Title, local.Title);
        Assert.Equal(DateTimeOffset.UnixEpoch, local.AddedAt);
        Assert.Single(_library.Tracks);
    }

    [Fact]
    public async Task Does_not_download_twice()
    {
        var remote = TestData.RemoteTrack("xyz");

        var first = await _useCase.ExecuteAsync(remote);
        var second = await _useCase.ExecuteAsync(remote);

        Assert.Equal(first, second);
        Assert.Equal(1, _plugin.DownloadCount);
    }

    [Fact]
    public async Task Keeps_the_download_when_the_shared_folder_refuses_it()
    {
        // A file where the music folder should be makes creating it fail, like a refused shared folder.
        Directory.CreateDirectory(Path.GetDirectoryName(_paths.MusicDirectory)!);
        await File.WriteAllTextAsync(_paths.MusicDirectory, "not a folder");

        var local = await _useCase.ExecuteAsync(TestData.RemoteTrack("xyz"));

        Assert.True(File.Exists(local.FilePath));
        Assert.StartsWith(Path.Combine(_paths.DataDirectory, "music"), local.FilePath);
    }

    public void Dispose()
    {
        if (File.Exists(_paths.MusicDirectory))
            File.Delete(_paths.MusicDirectory);
        _paths.Dispose();
    }
}
