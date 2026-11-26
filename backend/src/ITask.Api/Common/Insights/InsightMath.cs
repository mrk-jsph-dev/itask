namespace ITask.Api.Common.Insights;

/// <summary>A card's stay in one list, from <see cref="Start"/> up to <see cref="End"/>.</summary>
public sealed record ListSegment(Guid ListId, DateTimeOffset Start, DateTimeOffset End)
{
    public double Days => (End - Start).TotalDays;
}

/// <summary>A cross-list move read back from a <c>card.moved</c> entry, with list ids resolved.</summary>
public sealed record ListMove(DateTimeOffset At, Guid? FromListId, Guid? ToListId);

/// <summary>Card fields the list-segment rebuild needs.</summary>
public sealed record SegmentCard(
    Guid ListId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ListEnteredAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ArchivedAt);

/// <summary>Pure calculations behind board insights: percentiles, day buckets and list stays.</summary>
public static class InsightMath
{
    /// <summary>
    /// Linear-interpolated percentile (0 to 1) of <paramref name="values"/>, the same as a spreadsheet's
    /// PERCENTILE.INC; 0.5 is the usual median. Null when empty.
    /// </summary>
    public static double? Percentile(IEnumerable<double> values, double p)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(p, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(p, 1);
        var sorted = values.Order().ToArray();
        if (sorted.Length == 0)
        {
            return null;
        }

        var rank = p * (sorted.Length - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return sorted[low] + ((sorted[high] - sorted[low]) * (rank - low));
    }

    /// <summary>Rounds a day count to one decimal for display.</summary>
    public static double? Round(double? days) => days is { } d ? Math.Round(d, 1, MidpointRounding.AwayFromZero) : null;

    /// <summary>The local calendar date <paramref name="instant"/> falls on in <paramref name="zone"/>.</summary>
    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).Date);

    /// <summary>Counts per local day from <paramref name="from"/> to <paramref name="to"/> (inclusive); instants outside are ignored.</summary>
    public static IReadOnlyList<(DateOnly Date, int Count)> PerDay(
        IEnumerable<DateTimeOffset> instants, DateOnly from, DateOnly to, TimeZoneInfo zone)
    {
        var counts = new Dictionary<DateOnly, int>();
        foreach (var instant in instants)
        {
            var date = LocalDate(instant, zone);
            if (date >= from && date <= to)
            {
                counts[date] = counts.GetValueOrDefault(date) + 1;
            }
        }

        var days = new List<(DateOnly, int)>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            days.Add((date, counts.GetValueOrDefault(date)));
        }

        return days;
    }

    /// <summary>
    /// Rebuilds the lists a card stayed in. The first stay starts at creation in
    /// <paramref name="createdIn"/> (else the first move's source list, else the current list); each move
    /// closes a stay and opens the next. When the moves don't end in the current list, the current stay
    /// starts at <see cref="SegmentCard.ListEnteredAt"/>.
    /// Completion, archiving or <paramref name="now"/> stops the clock. Stays in unknown lists are dropped.
    /// </summary>
    public static IReadOnlyList<ListSegment> Segments(
        SegmentCard card, Guid? createdIn, IEnumerable<ListMove> moves, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);
        var ordered = moves.OrderBy(m => m.At).ToList();
        var stop = new[] { card.CompletedAt, card.ArchivedAt, now }.Where(d => d is not null).Min()!.Value;

        var segments = new List<ListSegment>();
        var list = createdIn ?? (ordered.Count > 0 ? ordered[0].FromListId : card.ListId);
        var start = card.CreatedAt;
        foreach (var move in ordered)
        {
            Add(list, start, move.At);
            list = move.ToListId;
            start = move.At;
        }

        if (list != card.ListId)
        {
            // The move into the current list is missing (trimmed or unresolved); close the last known stay there.
            Add(list, start, card.ListEnteredAt);
            start = card.ListEnteredAt;
        }

        Add(card.ListId, start, now);
        return segments;

        void Add(Guid? listId, DateTimeOffset from, DateTimeOffset to)
        {
            var end = to < stop ? to : stop;
            if (listId is { } id && end > from)
            {
                segments.Add(new ListSegment(id, from, end));
            }
        }
    }
}
