using Wired.Core.Abstractions;
using Wired.Core.Playback;
using Wired.Infrastructure.Storage;

namespace Wired.Infrastructure.Library;

public sealed class JsonPlaybackStateStore(IAppPaths paths) : IPlaybackStateStore
{
    private readonly JsonFile<PlaybackSnapshot> _file = new(Path.Combine(paths.DataDirectory, "playback.json"), StorageJsonContext.Default.ListPlaybackSnapshot);

    public Task<PlaybackSnapshot?> LoadAsync(CancellationToken cancellationToken = default) =>
        _file.ReadAsync(snapshots => snapshots.FirstOrDefault(), cancellationToken);

    public Task SaveAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken = default) =>
        _file.MutateAsync(
            snapshots =>
            {
                snapshots.Clear();
                snapshots.Add(snapshot);
            },
            cancellationToken);
}
