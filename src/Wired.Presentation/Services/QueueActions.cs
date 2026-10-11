using Wired.Core.Playback;

namespace Wired.Presentation.Services;

/// <summary>"Reproducir a continuación" / "Agregar a la cola" with an undo snackbar.</summary>
public sealed class QueueActions(IPlaybackService playback, IDialogService dialogs)
{
    public async Task PlayNextAsync(PlaybackItem item)
    {
        var wasIdle = playback.CurrentItem is null;
        await playback.PlayNextAsync([item]);
        if (wasIdle)
            return;

        var index = playback.CurrentIndex + 1;
        await dialogs.ShowSnackbarAsync($"\"{item.Title}\" sonará a continuación", "Deshacer", () => UndoAsync(item, index));
    }

    public async Task AddToQueueAsync(PlaybackItem item)
    {
        var wasIdle = playback.CurrentItem is null;
        await playback.AddToQueueAsync([item]);
        if (wasIdle)
            return;

        var index = playback.Queue.Count - 1;
        await dialogs.ShowSnackbarAsync($"\"{item.Title}\" agregada a la cola", "Deshacer", () => UndoAsync(item, index));
    }

    /// <summary>Removes the entry only if it is still where we put it (the queue may have moved on).</summary>
    private Task UndoAsync(PlaybackItem item, int index)
    {
        var queue = playback.Queue;
        return index < queue.Count && ReferenceEquals(queue[index], item) && index != playback.CurrentIndex
            ? playback.RemoveFromQueueAsync(index)
            : Task.CompletedTask;
    }
}
