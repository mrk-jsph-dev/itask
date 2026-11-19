using ITask.Api.Common.Cards;

namespace ITask.Api.Tests.Common;

public sealed class DueBucketsTests
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>Wednesday 2026-11-18, 23:30 in Manila (UTC+8).</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 15, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Today_is_the_local_calendar_day()
    {
        var (from, to) = DueBuckets.Today(Now, Manila);

        Assert.Equal(new DateTimeOffset(2026, 11, 17, 16, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 11, 18, 16, 0, 0, TimeSpan.Zero), to);
    }

    [Fact]
    public void Week_runs_from_today_to_the_end_of_sunday()
    {
        var (from, to) = DueBuckets.Week(Now, Manila);

        Assert.Equal(new DateTimeOffset(2026, 11, 17, 16, 0, 0, TimeSpan.Zero), from);
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 16, 0, 0, TimeSpan.Zero), to);
    }

    [Fact]
    public void Week_on_a_sunday_ends_that_night()
    {
        var sunday = new DateTimeOffset(2026, 11, 22, 4, 0, 0, TimeSpan.Zero);

        var (_, to) = DueBuckets.Week(sunday, Manila);

        Assert.Equal(new DateTimeOffset(2026, 11, 22, 16, 0, 0, TimeSpan.Zero), to);
    }

    [Fact]
    public void Day_across_a_dst_change_has_the_right_length()
    {
        var fallBack = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        var (from, to) = DueBuckets.Today(fallBack, NewYork);

        Assert.Equal(TimeSpan.FromHours(25), to - from);
    }

    [Theory]
    [InlineData(DueBucket.Overdue, -60, false, true)]
    [InlineData(DueBucket.Overdue, -60, true, false)]
    [InlineData(DueBucket.Overdue, 60, false, false)]
    [InlineData(DueBucket.Today, 20, false, true)]
    [InlineData(DueBucket.Today, 40, false, false)]
    [InlineData(DueBucket.Week, 40, false, true)]
    [InlineData(DueBucket.Week, 60 * 24 * 5, false, false)]
    [InlineData(DueBucket.None, 0, false, false)]
    public void Matches_buckets(DueBucket bucket, int minutesFromNow, bool completed, bool expected)
    {
        var due = Now.AddMinutes(minutesFromNow);

        Assert.Equal(expected, DueBuckets.Matches(bucket, due, completed ? Now : null, Now, Manila));
    }

    [Fact]
    public void None_matches_cards_without_a_date()
    {
        Assert.True(DueBuckets.Matches(DueBucket.None, null, null, Now, Manila));
        Assert.False(DueBuckets.Matches(DueBucket.Today, null, null, Now, Manila));
    }
}
