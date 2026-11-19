namespace ITask.Api.Common.Cards;

/// <summary>Due-date filter buckets. A card matches when it falls in any of the requested buckets.</summary>
public enum DueBucket
{
    /// <summary>Due before now and still open.</summary>
    Overdue,

    /// <summary>Due on the caller's local calendar day.</summary>
    Today,

    /// <summary>Due from the start of today up to the end of Sunday (ISO week), local time.</summary>
    Week,

    /// <summary>No due date.</summary>
    None,
}

/// <summary>Evaluates <see cref="DueBucket"/> membership in a given time zone.</summary>
public static class DueBuckets
{
    /// <summary>UTC range [from, to) of the local day containing <paramref name="now"/>.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Today(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var date = TimeZoneInfo.ConvertTime(now, zone).Date;
        return (Local(date, zone), Local(date.AddDays(1), zone));
    }

    /// <summary>UTC range [from, to) from the start of today to the end of this ISO week (Sunday).</summary>
    public static (DateTimeOffset From, DateTimeOffset To) Week(DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var date = TimeZoneInfo.ConvertTime(now, zone).Date;
        var daysToMonday = ((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7;
        var nextMonday = date.AddDays(daysToMonday == 0 ? 7 : daysToMonday);
        return (Local(date, zone), Local(nextMonday, zone));
    }

    public static bool Matches(
        DueBucket bucket, DateTimeOffset? dueAt, DateTimeOffset? completedAt, DateTimeOffset now, TimeZoneInfo zone) =>
        bucket switch
        {
            DueBucket.None => dueAt is null,
            DueBucket.Overdue => dueAt is { } d && d < now && completedAt is null,
            DueBucket.Today => dueAt is { } d && In(d, Today(now, zone)),
            DueBucket.Week => dueAt is { } d && In(d, Week(now, zone)),
            _ => false,
        };

    private static bool In(DateTimeOffset value, (DateTimeOffset From, DateTimeOffset To) range) =>
        value >= range.From && value < range.To;

    /// <summary>Local midnight of <paramref name="date"/> as a UTC instant; skipped (DST) times move forward.</summary>
    private static DateTimeOffset Local(DateTime date, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddMinutes(30);
        }

        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }
}
