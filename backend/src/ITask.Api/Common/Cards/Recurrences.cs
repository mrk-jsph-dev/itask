using ITask.Api.Common.Activity;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Cards;

/// <summary>A repeat rule without its card. <c>Weekdays</c> is a <see cref="CardRecurrence.Weekdays"/> bitmask.</summary>
public readonly record struct RecurrenceRule(RecurrenceKind Kind, int Weekdays, int? DayOfMonth)
{
    public static RecurrenceRule Of(CardRecurrence recurrence)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        return new RecurrenceRule(recurrence.Kind, recurrence.Weekdays, recurrence.DayOfMonth);
    }
}

/// <summary>A card's repeat rule as the API returns it. <c>Weekdays</c> are 0 (Sunday) to 6.</summary>
public sealed record RecurrenceView(
    RecurrenceKind Kind, IReadOnlyList<int> Weekdays, int? DayOfMonth, Guid TargetListId, DateTimeOffset NextRunAt)
{
    public static RecurrenceView From(CardRecurrence recurrence)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        return new RecurrenceView(
            recurrence.Kind,
            Recurrences.Days(recurrence.Weekdays),
            recurrence.DayOfMonth,
            recurrence.TargetListId,
            recurrence.NextRunAt);
    }
}

/// <summary>Occurrence dates of repeat rules and the copies they create.</summary>
public static class Recurrences
{
    /// <summary>Every bit of a weekday mask; a weekly rule with none of them repeats daily.</summary>
    public const int AllDays = 0b111_1111;

    public static int Mask(IEnumerable<int> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        return days.Aggregate(0, (mask, day) => mask | (1 << day));
    }

    public static IReadOnlyList<int> Days(int mask) => [.. Enumerable.Range(0, 7).Where(d => (mask & (1 << d)) != 0)];

    /// <summary>The first occurrence strictly after <paramref name="after"/>.</summary>
    public static DateOnly NextOccurrence(RecurrenceRule rule, DateOnly after)
    {
        switch (rule.Kind)
        {
            case RecurrenceKind.Weekly when (rule.Weekdays & AllDays) != 0:
                var date = after.AddDays(1);
                while (!OnWeekday(rule, date))
                {
                    date = date.AddDays(1);
                }

                return date;

            case RecurrenceKind.Monthly:
                var inMonth = InMonth(rule, after.Year, after.Month);
                if (inMonth > after)
                {
                    return inMonth;
                }

                var next = after.AddMonths(1);
                return InMonth(rule, next.Year, next.Month);

            default:
                return after.AddDays(1);
        }
    }

    /// <summary>The last occurrence on or before <paramref name="date"/>.</summary>
    public static DateOnly LatestOnOrBefore(RecurrenceRule rule, DateOnly date)
    {
        switch (rule.Kind)
        {
            case RecurrenceKind.Weekly when (rule.Weekdays & AllDays) != 0:
                var day = date;
                while (!OnWeekday(rule, day))
                {
                    day = day.AddDays(-1);
                }

                return day;

            case RecurrenceKind.Monthly:
                var inMonth = InMonth(rule, date.Year, date.Month);
                if (inMonth <= date)
                {
                    return inMonth;
                }

                var previous = date.AddMonths(-1);
                return InMonth(rule, previous.Year, previous.Month);

            default:
                return date;
        }
    }

    public static TimeZoneInfo Zone(string id) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    public static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary><paramref name="date"/> at <paramref name="time"/> local time, as UTC.</summary>
    public static DateTimeOffset At(DateOnly date, TimeOnly time, TimeZoneInfo zone) =>
        DueBuckets.Local(date.ToDateTime(time), zone);

    /// <summary>When a rule set at <paramref name="now"/> first creates a copy: midnight of the first occurrence after today.</summary>
    public static DateTimeOffset FirstRun(RecurrenceRule rule, DateTimeOffset now, TimeZoneInfo zone) =>
        At(NextOccurrence(rule, LocalDate(now, zone)), TimeOnly.MinValue, zone);

    /// <summary>
    /// The occurrence the next copy is for: the scheduled one when it is still ahead (the card was
    /// completed early), else the latest one that has started, so a long pause makes one copy, not many.
    /// </summary>
    public static DateOnly OccurrenceToSpawn(CardRecurrence recurrence, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(recurrence);
        var zone = Zone(recurrence.TimeZone);
        return recurrence.NextRunAt > now
            ? LocalDate(recurrence.NextRunAt, zone)
            : LatestOnOrBefore(RecurrenceRule.Of(recurrence), LocalDate(now, zone));
    }

    /// <summary>
    /// Adds the next copy of the recurrence's card (title, description, labels, assignees still on the
    /// board, checklist unticked, dates moved to the occurrence) and moves the rule onto it. Nothing is
    /// saved. Returns null, changing nothing, when neither the target list nor the card's list is active.
    /// </summary>
    public static async Task<Card?> SpawnNextAsync(
        AppDbContext db, IActivityRecorder activity, CardRecurrence recurrence, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(recurrence);

        var source = await db.Cards
            .AsNoTracking()
            .Where(c => c.Id == recurrence.CardId)
            .Select(c => new
            {
                c.BoardId,
                c.ListId,
                c.Number,
                c.Title,
                c.Emoji,
                c.Description,
                c.Priority,
                c.CoverColor,
                c.DueAt,
                c.StartAt,
                Labels = c.Labels.Select(l => l.LabelId).ToList(),
                Assignees = c.Assignees.Select(a => a.UserId).ToList(),
                Checklist = c.Checklist.OrderBy(i => i.Position).Select(i => new { i.Text, i.Position, i.DueAt, i.AssigneeId }).ToList(),
            })
            .FirstAsync(ct);

        var lists = await db.Lists
            .AsNoTracking()
            .Where(l => (l.Id == recurrence.TargetListId || l.Id == source.ListId) && l.BoardId == source.BoardId && l.ArchivedAt == null)
            .Select(l => new { l.Id, l.Name })
            .ToListAsync(ct);
        var list = lists.FirstOrDefault(l => l.Id == recurrence.TargetListId) ?? lists.FirstOrDefault();
        if (list is null)
        {
            return null;
        }

        var members = (await db.BoardMembers
                .Where(m => m.BoardId == source.BoardId)
                .Select(m => m.UserId)
                .ToListAsync(ct))
            .ToHashSet();

        var zone = Zone(recurrence.TimeZone);
        var rule = RecurrenceRule.Of(recurrence);
        var occurrence = OccurrenceToSpawn(recurrence, now);

        DateTimeOffset? dueAt = null;
        DateTimeOffset? startAt = null;
        TimeSpan? shift = null;
        if (source.DueAt is { } due)
        {
            dueAt = At(occurrence, TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(due, zone).DateTime), zone);
            shift = dueAt - due;
            startAt = source.StartAt + shift;
        }
        else if (source.StartAt is { } start)
        {
            startAt = At(occurrence, TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime), zone);
        }

        var board = await db.Boards.FirstAsync(b => b.Id == source.BoardId, ct);
        var last = await db.Cards
            .Where(c => c.ListId == list.Id && c.ArchivedAt == null)
            .MaxAsync(c => (double?)c.Position, ct);

        var copy = new Card
        {
            BoardId = source.BoardId,
            ListId = list.Id,
            Number = board.NextCardNumber,
            Title = source.Title,
            Emoji = source.Emoji,
            Description = source.Description,
            Priority = source.Priority,
            CoverColor = source.CoverColor,
            DueAt = dueAt,
            StartAt = startAt,
            Position = PositionCalculator.Append(last),
            CreatedById = recurrence.CreatedById,
            CreatedAt = now,
        };
        board.NextCardNumber++;
        copy.Labels.AddRange(source.Labels.Select(id => new CardLabel { LabelId = id }));
        copy.Assignees.AddRange(source.Assignees.Where(members.Contains).Select(id => new CardAssignee { UserId = id }));
        copy.Checklist.AddRange(source.Checklist.Select(i => new ChecklistItem
        {
            Text = i.Text,
            Position = i.Position,
            DueAt = shift is null ? null : i.DueAt + shift,
            AssigneeId = i.AssigneeId is { } a && members.Contains(a) ? a : null,
            CreatedAt = now,
        }));
        db.Cards.Add(copy);

        recurrence.CardId = copy.Id;
        recurrence.NextRunAt = At(NextOccurrence(rule, occurrence), TimeOnly.MinValue, zone);

        await activity.RecordCardAsync(
            ActivityTypes.CardCreated,
            source.BoardId,
            copy.Id,
            new { list = list.Name, listId = list.Id, recurringFrom = Card.Key(board.KeyPrefix, source.Number) },
            ct);
        return copy;
    }

    private static bool OnWeekday(RecurrenceRule rule, DateOnly date) =>
        (rule.Weekdays & (1 << (int)date.DayOfWeek)) != 0;

    private static DateOnly InMonth(RecurrenceRule rule, int year, int month) =>
        new(year, month, Math.Min(rule.DayOfMonth ?? 1, DateTime.DaysInMonth(year, month)));
}
