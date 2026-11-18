namespace ITask.Api.Common.Ordering;

/// <summary>
/// Fractional ordering with <c>double</c> positions: append adds <see cref="Step"/> to the last
/// position, inserts take the midpoint of their neighbours, and once a gap falls under
/// <see cref="MinGap"/> the whole sequence is renumbered to Step, 2·Step, ...
/// </summary>
public static class PositionCalculator
{
    public const double Step = 1024;

    public const double MinGap = 1e-6;

    /// <summary>Position after <paramref name="last"/>, or <see cref="Step"/> for an empty sequence.</summary>
    public static double Append(double? last) => (last ?? 0) + Step;

    /// <summary>
    /// Position between two neighbours. A missing left neighbour counts as 0; a missing right
    /// neighbour means append.
    /// </summary>
    public static double Between(double? before, double? after) => after is null
        ? Append(before)
        : ((before ?? 0) + after.Value) / 2;

    /// <summary>True when the gap between neighbours is too small to split reliably.</summary>
    public static bool NeedsRenumber(double? before, double? after) =>
        after is not null && after.Value - (before ?? 0) < MinGap;

    /// <summary>Fresh, evenly spaced positions for <paramref name="count"/> items.</summary>
    public static IReadOnlyList<double> Renumber(int count) =>
        Enumerable.Range(1, count).Select(i => i * Step).ToArray();
}
