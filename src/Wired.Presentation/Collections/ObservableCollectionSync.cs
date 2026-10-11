using System.Collections.ObjectModel;

namespace Wired.Presentation.Collections;

/// <summary>
/// Brings an <see cref="ObservableCollection{T}"/> to a target content with the fewest changes
/// (moves, inserts, removes) instead of clearing it. Lists bound to the UI keep their scroll
/// position and the rows the user is touching.
/// </summary>
public static class ObservableCollectionSync
{
    public static void SyncTo<T>(this ObservableCollection<T> collection, IReadOnlyList<T> target)
        where T : class
    {
        var wanted = new HashSet<T>(target, ReferenceEqualityComparer.Instance);

        // Remove what is gone, from the end so indexes stay valid.
        for (var index = collection.Count - 1; index >= 0; index--)
        {
            if (!wanted.Contains(collection[index]))
                collection.RemoveAt(index);
        }

        for (var position = 0; position < target.Count; position++)
        {
            var item = target[position];
            if (position < collection.Count && ReferenceEquals(collection[position], item))
                continue;

            var current = IndexOf(collection, item, position + 1);
            if (current >= 0)
                collection.Move(current, position);
            else
                collection.Insert(position, item);
        }
    }

    private static int IndexOf<T>(ObservableCollection<T> collection, T item, int start)
        where T : class
    {
        for (var index = start; index < collection.Count; index++)
        {
            if (ReferenceEquals(collection[index], item))
                return index;
        }

        return -1;
    }
}
