namespace Wired.Core.Playback;

/// <summary>
/// Ordered list of items with a cursor, shuffle and repeat. Pure logic, no audio involved.
/// </summary>
public sealed class PlaybackQueue(Random? random = null)
{
    private readonly Random _random = random ?? Random.Shared;
    private List<PlaybackItem> _original = [];
    private List<PlaybackItem> _order = [];

    /// <summary>Items in play order (shuffled when <see cref="IsShuffled"/>).</summary>
    public IReadOnlyList<PlaybackItem> Items => _order;

    public int CurrentIndex { get; private set; } = -1;

    public PlaybackItem? Current => CurrentIndex >= 0 ? _order[CurrentIndex] : null;

    public bool IsShuffled { get; private set; }

    public RepeatMode Repeat { get; set; }

    public bool HasNext => _order.Count > 0 && (CurrentIndex < _order.Count - 1 || Repeat == RepeatMode.All);

    public bool HasPrevious => _order.Count > 0 && (CurrentIndex > 0 || Repeat == RepeatMode.All);

    /// <summary>
    /// Replaces the queue and starts at <paramref name="startIndex"/> (an index of <paramref name="items"/>).
    /// When shuffle is on, the start item plays first and the rest are shuffled.
    /// </summary>
    public void Load(IEnumerable<PlaybackItem> items, int startIndex = 0)
    {
        _original = items.ToList();

        if (_original.Count == 0)
        {
            _order = [];
            CurrentIndex = -1;
            return;
        }

        ArgumentOutOfRangeException.ThrowIfNegative(startIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startIndex, _original.Count);

        _order = [.. _original];
        CurrentIndex = startIndex;
        if (IsShuffled)
            ShuffleKeepingCurrentFirst();
    }

    /// <summary>
    /// Puts back a queue saved earlier: <paramref name="order"/> is the play order and
    /// <paramref name="original"/> the order shuffle returns to (the same instances).
    /// </summary>
    public void Restore(IReadOnlyList<PlaybackItem> order, IReadOnlyList<PlaybackItem> original, int currentIndex, bool shuffled, RepeatMode repeat)
    {
        _order = [.. order];
        _original = original.Count == order.Count ? [.. original] : [.. order];
        IsShuffled = shuffled;
        Repeat = repeat;
        CurrentIndex = _order.Count == 0 ? -1 : Math.Clamp(currentIndex, 0, _order.Count - 1);
    }

    /// <summary>The order shuffle returns to, as positions in <see cref="Items"/>.</summary>
    public IReadOnlyList<int> OriginalOrder => _original.Select(item => IndexOfInstance(_order, item)).ToList();

    /// <summary>Loads <paramref name="items"/> in random order with shuffle turned on.</summary>
    public void LoadShuffled(IEnumerable<PlaybackItem> items)
    {
        var list = items.ToList();
        IsShuffled = true;
        Load(list, list.Count == 0 ? 0 : _random.Next(list.Count));
    }

    public void SetShuffle(bool enabled)
    {
        if (enabled == IsShuffled)
            return;

        IsShuffled = enabled;
        if (_order.Count == 0)
            return;

        if (enabled)
        {
            ShuffleKeepingCurrentFirst();
        }
        else
        {
            var current = Current;
            _order = [.. _original];
            CurrentIndex = current is null ? -1 : IndexOfInstance(_order, current);
        }
    }

    public bool MoveNext()
    {
        if (_order.Count == 0)
            return false;

        if (CurrentIndex < _order.Count - 1)
            CurrentIndex++;
        else if (Repeat == RepeatMode.All)
            CurrentIndex = 0;
        else
            return false;

        return true;
    }

    public bool MovePrevious()
    {
        if (_order.Count == 0)
            return false;

        if (CurrentIndex > 0)
            CurrentIndex--;
        else if (Repeat == RepeatMode.All)
            CurrentIndex = _order.Count - 1;
        else
            return false;

        return true;
    }

    /// <summary>Puts <paramref name="items"/> right after the current item ("play next").</summary>
    public void InsertNext(IReadOnlyList<PlaybackItem> items)
    {
        if (Current is not { } current)
        {
            Load(items);
            return;
        }

        _order.InsertRange(CurrentIndex + 1, items);
        _original.InsertRange(IndexOfInstance(_original, current) + 1, items);
    }

    /// <summary>Adds <paramref name="items"/> at the end of the queue.</summary>
    public void Append(IReadOnlyList<PlaybackItem> items)
    {
        if (Current is null)
        {
            Load(items);
            return;
        }

        _order.AddRange(items);
        _original.AddRange(items);
    }

    /// <summary>Reorders the play order (what the user sees in "A continuación").</summary>
    public void Move(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= _order.Count || to >= _order.Count)
            return;

        var item = _order[from];
        _order.RemoveAt(from);
        _order.Insert(to, item);

        if (from == CurrentIndex)
            CurrentIndex = to;
        else if (from < CurrentIndex && to >= CurrentIndex)
            CurrentIndex--;
        else if (from > CurrentIndex && to <= CurrentIndex)
            CurrentIndex++;

        if (!IsShuffled)
            _original = [.. _order];
    }

    /// <summary>Removes the entry at <paramref name="index"/> of the play order.</summary>
    /// <returns>True when the removed entry was the current one (the cursor now points to the next).</returns>
    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _order.Count)
            return false;

        var item = _order[index];
        _order.RemoveAt(index);
        _original.RemoveAt(IndexOfInstance(_original, item));

        var wasCurrent = index == CurrentIndex;
        if (_order.Count == 0)
            CurrentIndex = -1;
        else if (index < CurrentIndex)
            CurrentIndex--;
        else if (wasCurrent && CurrentIndex >= _order.Count)
            CurrentIndex = Repeat == RepeatMode.All ? 0 : _order.Count - 1;

        return wasCurrent;
    }

    /// <summary>Jumps to the entry at <paramref name="index"/> of the play order.</summary>
    public bool MoveTo(int index)
    {
        if (index < 0 || index >= _order.Count)
            return false;

        CurrentIndex = index;
        return true;
    }

    /// <summary>Swaps every entry of <paramref name="track"/> for its new version (new cover, title...).</summary>
    /// <returns>True when the queue contained it.</returns>
    public bool Replace(Models.LocalTrack track)
    {
        var found = false;
        for (var i = 0; i < _order.Count; i++)
        {
            if (_order[i].LocalTrack?.Id != track.Id)
                continue;

            var updated = PlaybackItem.FromLocal(track);
            var originalIndex = IndexOfInstance(_original, _order[i]);
            _order[i] = updated;
            if (originalIndex >= 0)
                _original[originalIndex] = updated;
            found = true;
        }

        return found;
    }

    /// <summary>Removes an item (e.g. deleted from the library) keeping the cursor consistent.</summary>
    public void Remove(Guid itemId)
    {
        _original.RemoveAll(i => i.Id == itemId);

        // Every copy goes (a track can be queued twice). Removing the current one leaves the
        // cursor on the entry that took its place.
        for (var index = _order.Count - 1; index >= 0; index--)
        {
            if (_order[index].Id != itemId)
                continue;

            _order.RemoveAt(index);
            if (index < CurrentIndex)
                CurrentIndex--;
        }

        if (_order.Count == 0)
            CurrentIndex = -1;
        else if (CurrentIndex >= _order.Count)
            CurrentIndex = _order.Count - 1;
    }

    /// <summary>Entries are records: the same track queued twice is equal, so find the exact instance.</summary>
    private static int IndexOfInstance(List<PlaybackItem> list, PlaybackItem item) =>
        list.FindIndex(i => ReferenceEquals(i, item));

    /// <summary>The current item moves to the front; everything else is shuffled behind it.</summary>
    private void ShuffleKeepingCurrentFirst()
    {
        var current = _order[CurrentIndex];
        var rest = _order.Where((_, index) => index != CurrentIndex).ToArray();
        _random.Shuffle(rest);

        _order = [current, .. rest];
        CurrentIndex = 0;
    }
}
