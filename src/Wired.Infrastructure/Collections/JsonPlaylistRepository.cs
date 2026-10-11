using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Infrastructure.Storage;

namespace Wired.Infrastructure.Collections;

public sealed class JsonPlaylistRepository(IAppPaths paths) : IPlaylistRepository
{
    private readonly JsonFile<Playlist> _file = new(Path.Combine(paths.DataDirectory, "playlists.json"), StorageJsonContext.Default.ListPlaylist);

    public Task<IReadOnlyList<Playlist>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _file.ReadAsync<IReadOnlyList<Playlist>>(playlists => playlists.ToList(), cancellationToken);

    public Task SaveAsync(Playlist playlist, CancellationToken cancellationToken = default) =>
        _file.MutateAsync(
            playlists =>
            {
                var index = playlists.FindIndex(p => p.Id == playlist.Id);
                if (index >= 0)
                    playlists[index] = playlist;
                else
                    playlists.Add(playlist);
            },
            cancellationToken);

    public Task DeleteAsync(Guid playlistId, CancellationToken cancellationToken = default) =>
        _file.MutateAsync(playlists => playlists.RemoveAll(p => p.Id == playlistId), cancellationToken);
}
