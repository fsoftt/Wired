using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wired.Core.Localization;
using Wired.Core.Playback;
using Wired.Presentation.Collections;
using Wired.Presentation.Services;

namespace Wired.Presentation.ViewModels;

/// <summary>"A continuación": what plays now and what comes next, reorderable.</summary>
public sealed partial class QueueViewModel(IPlaybackService playback, IUiDispatcher dispatcher) : LocalizedObservableObject
{
    private bool _isActive;
    private bool _isApplyingReorder;

    public ObservableCollection<QueueEntryViewModel> Upcoming { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCurrent))]
    public partial PlaybackItem? Current { get; private set; }

    public bool HasCurrent => Current is not null;

    public bool HasUpcoming => Upcoming.Count > 0;

    public string Summary => Upcoming.Count switch
    {
        0 => L.T("Queue_Empty"),
        var n => L.N("QueueUpcoming", n),
    };

    public void Activate()
    {
        if (!_isActive)
        {
            _isActive = true;
            playback.StateChanged += OnPlaybackChanged;
        }

        Refresh();
    }

    public void Deactivate()
    {
        _isActive = false;
        playback.StateChanged -= OnPlaybackChanged;
    }

    /// <summary>
    /// Called by the view after the user dragged entries: applies the new visual order to the queue
    /// as a series of moves.
    /// </summary>
    public void ApplyReorder()
    {
        // Snapshot first: every move raises StateChanged, and a refresh must not rebuild the list
        // the user just arranged before all moves are applied.
        var desired = Upcoming.Select(e => e.QueueIndex).ToList();
        var offset = playback.CurrentIndex + 1;
        var working = desired.Order().ToList();

        _isApplyingReorder = true;
        try
        {
            for (var position = 0; position < desired.Count; position++)
            {
                var from = working.IndexOf(desired[position]);
                if (from == position)
                    continue;

                playback.MoveInQueue(offset + from, offset + position);
                working.RemoveAt(from);
                working.Insert(position, desired[position]);
            }
        }
        finally
        {
            _isApplyingReorder = false;
        }

        Refresh();
    }

    public Task SkipToAsync(QueueEntryViewModel entry) => playback.SkipToAsync(entry.QueueIndex);

    public Task RemoveAsync(QueueEntryViewModel entry) => playback.RemoveFromQueueAsync(entry.QueueIndex);

    [RelayCommand]
    private async Task ClearAsync()
    {
        // From the end so indexes of the remaining entries do not shift.
        for (var index = playback.Queue.Count - 1; index > playback.CurrentIndex; index--)
            await playback.RemoveFromQueueAsync(index);
    }

    private void OnPlaybackChanged(object? sender, EventArgs e)
    {
        if (!_isApplyingReorder)
            dispatcher.Post(Refresh);
    }

    private void Refresh()
    {
        var queue = playback.Queue;
        var current = playback.CurrentIndex;
        Current = current >= 0 && current < queue.Count ? queue[current] : null;

        // Reuse rows that did not change (play/pause also refreshes): no jumps while dragging.
        var existing = Upcoming.ToDictionary(e => (e.QueueIndex, e.Item), ReferenceTupleComparer.Instance);
        var target = new List<QueueEntryViewModel>();
        for (var index = current + 1; index < queue.Count; index++)
        {
            target.Add(existing.TryGetValue((index, queue[index]), out var entry)
                ? entry
                : new QueueEntryViewModel(queue[index], index, this));
        }

        Upcoming.SyncTo(target);

        OnPropertyChanged(nameof(HasUpcoming));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>Same position and same queue entry instance (equal records may be queued twice).</summary>
    private sealed class ReferenceTupleComparer : IEqualityComparer<(int Index, PlaybackItem Item)>
    {
        public static readonly ReferenceTupleComparer Instance = new();

        public bool Equals((int Index, PlaybackItem Item) x, (int Index, PlaybackItem Item) y) =>
            x.Index == y.Index && ReferenceEquals(x.Item, y.Item);

        public int GetHashCode((int Index, PlaybackItem Item) obj) =>
            HashCode.Combine(obj.Index, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Item));
    }
}
