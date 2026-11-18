namespace ITask.Api.Common.Ordering;

/// <summary>
/// Where a moved item goes, given the ids of the items that end up immediately before and after it.
/// </summary>
public static class NeighbourSlot
{
    /// <summary>
    /// Index in <paramref name="others"/> (the ordered items without the moved one) for the moved
    /// item, or null when a neighbour is unknown or the two are not adjacent. Neither id means the end.
    /// </summary>
    public static int? Find<T>(IReadOnlyList<T> others, Func<T, Guid> id, Guid? beforeId, Guid? afterId)
    {
        ArgumentNullException.ThrowIfNull(others);
        ArgumentNullException.ThrowIfNull(id);

        var ids = others.Select(id).ToList();
        var beforeIndex = beforeId is { } b ? ids.IndexOf(b) : -1;
        var afterIndex = afterId is { } a ? ids.IndexOf(a) : ids.Count;

        if ((beforeId is not null && beforeIndex < 0) || (afterId is not null && afterIndex < 0))
        {
            return null;
        }

        return (beforeId, afterId) switch
        {
            (null, null) => ids.Count,
            (not null, null) => beforeIndex + 1,
            (null, not null) => afterIndex,
            _ => afterIndex == beforeIndex + 1 ? afterIndex : null,
        };
    }

    /// <summary>
    /// Inserts <paramref name="moved"/> at <paramref name="index"/> of <paramref name="others"/> and
    /// sets its position, renumbering every item when the gap is too small. Returns true on renumber.
    /// </summary>
    public static bool Place<T>(List<T> others, T moved, int index, Func<T, double> get, Action<T, double> set)
    {
        ArgumentNullException.ThrowIfNull(others);
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        double? before = index > 0 ? get(others[index - 1]) : null;
        double? after = index < others.Count ? get(others[index]) : null;
        others.Insert(index, moved);

        if (!PositionCalculator.NeedsRenumber(before, after))
        {
            set(moved, PositionCalculator.Between(before, after));
            return false;
        }

        var positions = PositionCalculator.Renumber(others.Count);
        for (var i = 0; i < others.Count; i++)
        {
            set(others[i], positions[i]);
        }

        return true;
    }
}
