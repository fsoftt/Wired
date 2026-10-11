using Wired.Core.Abstractions;
using Wired.Core.Collections;

namespace Wired.Core.UseCases;

/// <summary>
/// Cleans libraries where the same song was added more than once (a download and a folder scan
/// racing in older versions). Playlists and likes are moved to the copy that is kept.
/// Runs once per app start; every caller awaits the same run.
/// </summary>
public sealed class RemoveDuplicateTracksUseCase(ITrackLibrary library, PlaylistService playlists, LikesService likes)
{
    private readonly Lock _gate = new();
    private Task<int>? _run;

    /// <returns>Number of duplicates removed.</returns>
    public Task<int> ExecuteAsync()
    {
        lock (_gate)
            return _run ??= Task.Run(RunAsync);
    }

    private async Task<int> RunAsync()
    {
        var replaced = await library.RemoveDuplicatesAsync();
        if (replaced.Count == 0)
            return 0;

        foreach (var playlist in await playlists.GetAllAsync())
        {
            if (playlist.TrackIds.Any(replaced.ContainsKey))
                await playlists.SetTracksAsync(playlist, playlist.TrackIds.Select(id => replaced.GetValueOrDefault(id, id)));
        }

        await likes.InitializeAsync();
        foreach (var (duplicate, kept) in replaced)
        {
            if (!likes.IsLiked(duplicate))
                continue;

            await likes.SetLikedAsync(kept, true);
            await likes.SetLikedAsync(duplicate, false);
        }

        return replaced.Count;
    }
}
