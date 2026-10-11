using Wired.Core.Abstractions;
using Wired.Core.Import;
using Wired.Infrastructure.Storage;

namespace Wired.Infrastructure.Collections;

public sealed class JsonImportSessionRepository(IAppPaths paths) : IImportSessionRepository
{
    private readonly JsonFile<PlaylistImportSession> _file = new(Path.Combine(paths.DataDirectory, "imports.json"), StorageJsonContext.Default.ListPlaylistImportSession);

    public Task<IReadOnlyList<PlaylistImportSession>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _file.ReadAsync<IReadOnlyList<PlaylistImportSession>>(sessions => sessions.OrderByDescending(s => s.CreatedAt).ToList(), cancellationToken);

    public Task SaveAsync(PlaylistImportSession session, CancellationToken cancellationToken = default) =>
        _file.MutateAsync(
            sessions =>
            {
                var index = sessions.FindIndex(s => s.Id == session.Id);
                if (index >= 0)
                    sessions[index] = session;
                else
                    sessions.Add(session);
            },
            cancellationToken);

    public Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        _file.MutateAsync(sessions => sessions.RemoveAll(s => s.Id == sessionId), cancellationToken);
}
