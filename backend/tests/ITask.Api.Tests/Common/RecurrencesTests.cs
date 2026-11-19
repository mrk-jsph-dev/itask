using System.Globalization;
using ITask.Api.Common.Cards;
using ITask.Api.Domain;

namespace ITask.Api.Tests.Common;

public sealed class RecurrencesTests
{
    private static readonly RecurrenceRule Daily = new(RecurrenceKind.Daily, 0, null);

    // Monday and Wednesday.
    private static readonly RecurrenceRule MonWed = new(RecurrenceKind.Weekly, Recurrences.Mask([1, 3]), null);

    private static DateOnly D(string value) => DateOnly.Parse(value, CultureInfo.InvariantCulture);

    private static RecurrenceRule Monthly(int day) => new(RecurrenceKind.Monthly, 0, day);

    [Theory]
    [InlineData("2030-03-04", "2030-03-06")] // Mon -> Wed
    [InlineData("2030-03-06", "2030-03-11")] // Wed -> next Mon
    [InlineData("2030-03-09", "2030-03-11")] // Sat -> Mon
    public void Weekly_finds_the_next_listed_day(string after, string expected) =>
        Assert.Equal(D(expected), Recurrences.NextOccurrence(MonWed, D(after)));

    [Theory]
    [InlineData(31, "2030-01-31", "2030-02-28")]
    [InlineData(31, "2030-02-28", "2030-03-31")]
    [InlineData(15, "2030-01-10", "2030-01-15")]
    [InlineData(15, "2030-01-15", "2030-02-15")]
    [InlineData(29, "2032-01-30", "2032-02-29")]
    public void Monthly_clamps_to_the_last_day(int day, string after, string expected) =>
        Assert.Equal(D(expected), Recurrences.NextOccurrence(Monthly(day), D(after)));

    [Fact]
    public void Daily_is_the_next_day_and_latest_is_today() =>
        Assert.Equal(
            (new DateOnly(2030, 3, 5), new DateOnly(2030, 3, 4)),
            (Recurrences.NextOccurrence(Daily, new DateOnly(2030, 3, 4)), Recurrences.LatestOnOrBefore(Daily, new DateOnly(2030, 3, 4))));

    [Theory]
    [InlineData("2030-03-10", "2030-03-06")] // Sun -> Wed before
    [InlineData("2030-03-11", "2030-03-11")] // Mon itself
    public void Latest_weekly_looks_back(string date, string expected) =>
        Assert.Equal(D(expected), Recurrences.LatestOnOrBefore(MonWed, D(date)));

    [Fact]
    public void Latest_monthly_falls_back_to_the_previous_month() =>
        Assert.Equal(new DateOnly(2030, 2, 28), Recurrences.LatestOnOrBefore(Monthly(31), new DateOnly(2030, 3, 30)));

    [Fact]
    public void A_weekly_rule_without_days_repeats_daily() =>
        Assert.Equal(
            new DateOnly(2030, 3, 5),
            Recurrences.NextOccurrence(new RecurrenceRule(RecurrenceKind.Weekly, 0, null), new DateOnly(2030, 3, 4)));

    [Fact]
    public void First_run_is_local_midnight_of_the_next_occurrence()
    {
        var zone = Recurrences.Zone("Asia/Manila"); // UTC+8, no DST
        var now = new DateTimeOffset(2030, 3, 4, 20, 0, 0, TimeSpan.Zero); // Tue 04:00 in Manila

        Assert.Equal(new DateTimeOffset(2030, 3, 5, 16, 0, 0, TimeSpan.Zero), Recurrences.FirstRun(MonWed, now, zone));
    }

    [Theory]
    [InlineData("America/Santiago")]
    [InlineData("America/Havana")]
    [InlineData("Europe/Berlin")]
    public void Local_midnight_stays_on_its_date_through_dst_changes(string id)
    {
        // Santiago and Havana change their clocks at midnight, so some local midnights don't exist.
        var zone = Recurrences.Zone(id);
        var first = new DateOnly(2030, 1, 1);
        for (var date = first; date.Year == 2030; date = date.AddDays(1))
        {
            Assert.Equal(date, Recurrences.LocalDate(Recurrences.At(date, TimeOnly.MinValue, zone), zone));
        }
    }

    [Fact]
    public void Spawning_after_a_long_pause_targets_the_latest_occurrence()
    {
        var recurrence = new CardRecurrence
        {
            Kind = RecurrenceKind.Weekly,
            Weekdays = Recurrences.Mask([1, 3]),
            NextRunAt = new DateTimeOffset(2030, 3, 4, 0, 0, 0, TimeSpan.Zero),
        };

        Assert.Equal(
            new DateOnly(2030, 3, 13),
            Recurrences.OccurrenceToSpawn(recurrence, new DateTimeOffset(2030, 3, 15, 9, 0, 0, TimeSpan.Zero)));
        Assert.Equal(
            new DateOnly(2030, 3, 4),
            Recurrences.OccurrenceToSpawn(recurrence, new DateTimeOffset(2030, 3, 1, 9, 0, 0, TimeSpan.Zero)));
    }
}
