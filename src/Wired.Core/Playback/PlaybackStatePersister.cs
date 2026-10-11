using Wired.Core.Abstractions;

namespace Wired.Core.Playback;

/// <summary>
/// Remembers the queue, the track and the position, and puts them back when the app is reopened
/// (paused, ready to continue). Saves shortly after every change and every few seconds while playing.
/// </summary>
public sealed class PlaybackStatePersister(IPlaybackService playback, IPlaybackStateStore store, ITrackLibrary library)
{
    /// <summary>Changes often come in bursts (a whole album queued): save once they settle.</summary>
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan PositionInterval = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private int _savePending;
    private bool _started;

    /// <summary>Restores the saved queue (if nothing is playing yet), then keeps it saved.</summary>
    public async Task StartAsync()
    {
        if (_started)
            return;
        _started = true;

        try
        {
            await RestoreAsync();
        }
        catch (Exception)
        {
            // A corrupted or outdated file only costs the resumed queue.
        }

        playback.StateChanged += (_, _) => ScheduleSave();
        _ = Task.Run(SavePositionWhilePlayingAsync);
    }

    public async Task RestoreAsync()
    {
        var snapshot = await store.LoadAsync();
        if (snapshot is null || snapshot.Entries.Count == 0)
            return;

        var tracks = (await library.GetAllAsync()).ToDictionary(t => t.Id);

        // Downloads deleted meanwhile are skipped; positions are remapped to what is left.
        var items = new List<PlaybackItem>();
        var newIndex = new int[snapshot.Entries.Count];
        for (var index = 0; index < snapshot.Entries.Count; index++)
        {
            var item = ToItem(snapshot.Entries[index], tracks);
            newIndex[index] = item is null ? -1 : items.Count;
            if (item is not null)
                items.Add(item);
        }

        if (items.Count == 0)
            return;

        var current = Math.Clamp(snapshot.CurrentIndex, 0, snapshot.Entries.Count - 1);
        var currentGone = newIndex[current] < 0;
        var currentIndex = Enumerable.Range(current, snapshot.Entries.Count - current)
            .Select(i => newIndex[i])
            .FirstOrDefault(i => i >= 0, items.Count - 1);

        var original = snapshot.OriginalOrder
            .Where(i => i >= 0 && i < newIndex.Length && newIndex[i] >= 0)
            .Select(i => newIndex[i])
            .ToList();

        playback.Restore(items, original, currentIndex, currentGone ? TimeSpan.Zero : snapshot.Position, snapshot.Shuffle, snapshot.Repeat);
    }

    /// <summary>Saves right away (e.g. the app goes to background).</summary>
    public async Task SaveNowAsync()
    {
        if (!_started)
            return;

        await _saveGate.WaitAsync();
        try
        {
            await store.SaveAsync(TakeSnapshot());
        }
        catch (Exception)
        {
            // Best effort: the next change saves again.
        }
        finally
        {
            _saveGate.Release();
        }
    }

    public PlaybackSnapshot TakeSnapshot()
    {
        var queue = playback.Queue;
        return new PlaybackSnapshot(
            queue.Select(item => new PlaybackSnapshotEntry(item.LocalTrack?.Id, item.LocalTrack is null ? item.RemoteTrack : null)).ToList(),
            playback.QueueOriginalOrder,
            playback.CurrentIndex,
            playback.Position,
            playback.IsShuffleEnabled,
            playback.RepeatMode);
    }

    private static PlaybackItem? ToItem(PlaybackSnapshotEntry entry, IReadOnlyDictionary<Guid, Models.LocalTrack> tracks)
    {
        if (entry.LocalTrackId is { } id)
            return tracks.TryGetValue(id, out var track) ? PlaybackItem.FromLocal(track) : null;

        return entry.Remote is { } remote ? PlaybackItem.FromStream(remote) : null;
    }

    private void ScheduleSave()
    {
        if (Interlocked.Exchange(ref _savePending, 1) == 1)
            return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(SaveDelay);
            Volatile.Write(ref _savePending, 0);
            await SaveNowAsync();
        });
    }

    private async Task SavePositionWhilePlayingAsync()
    {
        using var timer = new PeriodicTimer(PositionInterval);
        while (await timer.WaitForNextTickAsync())
        {
            if (playback.IsPlaying)
                await SaveNowAsync();
        }
    }
}
