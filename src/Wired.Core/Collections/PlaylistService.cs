using Wired.Core.Abstractions;
using Wired.Core.Models;

namespace Wired.Core.Collections;

/// <summary>
/// Creates and edits playlists. Raises <see cref="PlaylistsChanged"/> after every change
/// so every screen showing playlists stays in sync.
/// </summary>
public sealed class PlaylistService(IPlaylistRepository repository, ITrackLibrary library, IClock clock)
{
    public event EventHandler? PlaylistsChanged;

    /// <summary>Raised after <see cref="PlaylistsChanged"/> when a playlist is deleted.</summary>
    public event EventHandler<Playlist>? PlaylistDeleted;

    public async Task<IReadOnlyList<Playlist>> GetAllAsync(CancellationToken cancellationToken = default) =>
        (await repository.GetAllAsync(cancellationToken)).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public async Task<Playlist?> FindAsync(Guid playlistId, CancellationToken cancellationToken = default) =>
        (await repository.GetAllAsync(cancellationToken)).FirstOrDefault(p => p.Id == playlistId);

    /// <exception cref="ArgumentException">The name is empty or too long.</exception>
    public async Task<Playlist> CreateAsync(string name, CancellationToken cancellationToken = default)
    {
        var playlist = new Playlist(Guid.NewGuid(), NormalizeName(name), [], clock.Now);
        await SaveAsync(playlist, cancellationToken);
        return playlist;
    }

    /// <exception cref="ArgumentException">The name is empty or too long.</exception>
    public Task<Playlist> RenameAsync(Playlist playlist, string name, CancellationToken cancellationToken = default) =>
        UpdateAsync(playlist.Id, p => p with { Name = NormalizeName(name) }, cancellationToken);

    public async Task DeleteAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        await repository.DeleteAsync(playlist.Id, cancellationToken);
        PlaylistsChanged?.Invoke(this, EventArgs.Empty);
        PlaylistDeleted?.Invoke(this, playlist);
    }

    /// <summary>Adds a playlist restored from a backup, keeping its id.</summary>
    public Task ImportAsync(Playlist playlist, CancellationToken cancellationToken = default) =>
        SaveAsync(playlist, cancellationToken);

    /// <summary>Appends the track; adding a track that is already in the playlist does nothing.</summary>
    public Task<Playlist> AddTrackAsync(Playlist playlist, LocalTrack track, CancellationToken cancellationToken = default) =>
        UpdateAsync(
            playlist.Id,
            p => p.Contains(track.Id) ? p : p with { TrackIds = [.. p.TrackIds, track.Id] },
            cancellationToken);

    /// <summary>Replaces the tracks of the playlist (duplicates are dropped).</summary>
    public Task<Playlist> SetTracksAsync(Playlist playlist, IEnumerable<Guid> trackIds, CancellationToken cancellationToken = default)
    {
        var ids = trackIds.Distinct().ToList();
        return UpdateAsync(
            playlist.Id,
            p => p.TrackIds.SequenceEqual(ids) ? p : p with { TrackIds = ids },
            cancellationToken);
    }

    public Task<Playlist> RemoveTrackAsync(Playlist playlist, Guid trackId, CancellationToken cancellationToken = default) =>
        UpdateAsync(
            playlist.Id,
            p => p with { TrackIds = p.TrackIds.Where(id => id != trackId).ToList() },
            cancellationToken);

    /// <summary>Called when a track is deleted from the library.</summary>
    public async Task RemoveTrackFromAllAsync(Guid trackId, CancellationToken cancellationToken = default)
    {
        var changed = false;
        foreach (var playlist in await repository.GetAllAsync(cancellationToken))
        {
            if (!playlist.Contains(trackId))
                continue;

            await repository.SaveAsync(playlist with { TrackIds = playlist.TrackIds.Where(id => id != trackId).ToList() }, cancellationToken);
            changed = true;
        }

        if (changed)
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Tracks of the playlist in order. Tracks no longer in the library are skipped.</summary>
    public async Task<IReadOnlyList<LocalTrack>> GetTracksAsync(Playlist playlist, CancellationToken cancellationToken = default)
    {
        var tracksById = (await library.GetAllAsync(cancellationToken)).ToDictionary(t => t.Id);
        return playlist.TrackIds
            .Select(id => tracksById.GetValueOrDefault(id))
            .OfType<LocalTrack>()
            .ToList();
    }

    public static string NormalizeName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            throw new ArgumentException("El nombre de la lista no puede estar vacío.", nameof(name));
        if (trimmed.Length > Playlist.MaxNameLength)
            throw new ArgumentException($"El nombre no puede superar los {Playlist.MaxNameLength} caracteres.", nameof(name));

        return trimmed;
    }

    private async Task<Playlist> UpdateAsync(Guid playlistId, Func<Playlist, Playlist> change, CancellationToken cancellationToken)
    {
        var current = await FindAsync(playlistId, cancellationToken)
            ?? throw new InvalidOperationException($"Playlist '{playlistId}' does not exist.");

        var updated = change(current);
        if (updated != current)
            await SaveAsync(updated, cancellationToken);

        return updated;
    }

    private async Task SaveAsync(Playlist playlist, CancellationToken cancellationToken)
    {
        await repository.SaveAsync(playlist, cancellationToken);
        PlaylistsChanged?.Invoke(this, EventArgs.Empty);
    }
}
