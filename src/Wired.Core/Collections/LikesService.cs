using Wired.Core.Abstractions;
using Wired.Core.Models;

namespace Wired.Core.Collections;

public sealed class LikeChangedEventArgs(Guid trackId, bool isLiked) : EventArgs
{
    public Guid TrackId { get; } = trackId;

    public bool IsLiked { get; } = isLiked;
}

/// <summary>
/// The "Me gusta" collection. Liked ids are cached in memory so the UI can ask synchronously.
/// </summary>
public sealed class LikesService(ILikedTracksRepository repository, ITrackLibrary library)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HashSet<Guid>? _likedIds;

    public event EventHandler<LikeChangedEventArgs>? LikeChanged;

    /// <summary>Loads the liked ids. Must be awaited once before <see cref="IsLiked"/> is reliable.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_likedIds is not null)
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            _likedIds ??= (await repository.GetAllAsync(cancellationToken)).ToHashSet();
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool IsLiked(Guid trackId) => _likedIds?.Contains(trackId) ?? false;

    public int Count => _likedIds?.Count ?? 0;

    /// <returns>The new liked state.</returns>
    public async Task<bool> ToggleAsync(LocalTrack track, CancellationToken cancellationToken = default)
    {
        var liked = !IsLiked(track.Id);
        await SetLikedAsync(track.Id, liked, cancellationToken);
        return liked;
    }

    public async Task SetLikedAsync(Guid trackId, bool liked, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (liked == _likedIds!.Contains(trackId))
                return;

            await repository.SetLikedAsync(trackId, liked, cancellationToken);
            if (liked)
                _likedIds.Add(trackId);
            else
                _likedIds.Remove(trackId);
        }
        finally
        {
            _gate.Release();
        }

        LikeChanged?.Invoke(this, new LikeChangedEventArgs(trackId, liked));
    }

    /// <summary>Liked tracks, most recently liked first.</summary>
    public async Task<IReadOnlyList<LocalTrack>> GetLikedTracksAsync(CancellationToken cancellationToken = default)
    {
        var likedIds = await repository.GetAllAsync(cancellationToken);
        var tracksById = (await library.GetAllAsync(cancellationToken)).ToDictionary(t => t.Id);

        return likedIds
            .Select(id => tracksById.GetValueOrDefault(id))
            .OfType<LocalTrack>()
            .ToList();
    }
}
