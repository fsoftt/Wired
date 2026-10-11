using Wired.Core.Abstractions;
using Wired.Core.Collections;
using Wired.Infrastructure.Storage;

namespace Wired.Infrastructure.Collections;

public sealed class JsonLikedTracksRepository(IAppPaths paths) : ILikedTracksRepository
{
    private readonly JsonFile<Guid> _file = new(Path.Combine(paths.DataDirectory, "likes.json"), StorageJsonContext.Default.ListGuid);

    public Task<IReadOnlyList<Guid>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _file.ReadAsync<IReadOnlyList<Guid>>(ids => ids.ToList(), cancellationToken);

    public Task SetLikedAsync(Guid trackId, bool liked, CancellationToken cancellationToken = default) =>
        _file.MutateAsync(
            ids =>
            {
                ids.Remove(trackId);
                if (liked)
                    ids.Insert(0, trackId); // Most recent first.
            },
            cancellationToken);
}
