using Wired.Core.Abstractions;
using Wired.Core.Models;
using Wired.Infrastructure.Storage;

namespace Wired.Infrastructure.Library;

/// <summary>
/// Stores the library as a JSON file. Simple and good enough for a personal collection;
/// can be swapped for SQLite without touching any other layer.
/// </summary>
public sealed class JsonTrackLibrary(IAppPaths paths) : ITrackLibrary
{
    private readonly JsonFile<LocalTrack> _file = new(Path.Combine(paths.DataDirectory, "library.json"), StorageJsonContext.Default.ListLocalTrack);

    /// <summary>
    /// Unreachable files are hidden but never forgotten: after a reinstall, files in the shared music
    /// folder are invisible until the user grants read access again.
    /// </summary>
    public Task<IReadOnlyList<LocalTrack>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _file.ReadAsync<IReadOnlyList<LocalTrack>>(
            tracks => tracks.Where(t => File.Exists(t.FilePath)).OrderByDescending(t => t.AddedAt).ToList(),
            cancellationToken);

    public Task<IReadOnlyList<LocalTrack>> GetAllStoredAsync(CancellationToken cancellationToken = default) =>
        _file.ReadAsync<IReadOnlyList<LocalTrack>>(tracks => tracks.ToList(), cancellationToken);

    public Task<LocalTrack?> FindAsync(string sourceId, string remoteId, CancellationToken cancellationToken = default) =>
        _file.ReadAsync(
            tracks => tracks.FirstOrDefault(t => t.SourceId == sourceId && t.RemoteId == remoteId),
            cancellationToken);

    public Task<LocalTrack> AddAsync(LocalTrack track, CancellationToken cancellationToken = default) =>
        _file.MutateAsync(
            tracks =>
            {
                // Checked under the same lock as the add: a download and a folder scan finishing
                // at the same time must not both add the song.
                var existing = tracks.FirstOrDefault(t => t.SourceId == track.SourceId && t.RemoteId == track.RemoteId);
                if (existing is not null)
                    return (existing, false);

                tracks.Add(track);
                return (track, true);
            },
            cancellationToken);

    public Task<IReadOnlyDictionary<Guid, Guid>> RemoveDuplicatesAsync(CancellationToken cancellationToken = default) =>
        _file.MutateAsync<IReadOnlyDictionary<Guid, Guid>>(
            tracks =>
            {
                var replaced = new Dictionary<Guid, Guid>();
                foreach (var group in tracks.GroupBy(t => (t.SourceId, t.RemoteId)).Where(g => g.Count() > 1))
                {
                    var kept = PickBest(group);
                    foreach (var duplicate in group.Where(t => t.Id != kept.Id))
                        replaced[duplicate.Id] = kept.Id;
                }

                if (replaced.Count > 0)
                    tracks.RemoveAll(t => replaced.ContainsKey(t.Id));
                return (replaced, replaced.Count > 0);
            },
            cancellationToken);

    /// <summary>The copy worth keeping: playable, with the best metadata, the oldest.</summary>
    private static LocalTrack PickBest(IEnumerable<LocalTrack> copies) =>
        copies
            .OrderByDescending(t => File.Exists(t.FilePath))
            .ThenByDescending(t => t.IsEnriched)
            .ThenByDescending(t => t.ArtworkPath is not null)
            .ThenByDescending(t => t.Duration is not null)
            .ThenBy(t => t.AddedAt)
            .First();

    public Task RemoveAsync(Guid trackId, CancellationToken cancellationToken = default) =>
        _file.MutateAsync(tracks => tracks.RemoveAll(t => t.Id == trackId), cancellationToken);

    public Task UpdateAsync(LocalTrack track, CancellationToken cancellationToken = default) =>
        _file.MutateAsync(
            tracks =>
            {
                var index = tracks.FindIndex(t => t.Id == track.Id);
                if (index >= 0)
                    tracks[index] = track;
            },
            cancellationToken);
}
