using ITask.Api.Common.Insights;

namespace ITask.Api.Tests.Common;

public sealed class InsightMathTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Todo = Guid.NewGuid();
    private static readonly Guid Doing = Guid.NewGuid();
    private static readonly Guid Done = Guid.NewGuid();

    private static DateTimeOffset DaysAgo(double days) => Now.AddDays(-days);

    [Fact]
    public void Percentile_interpolates_between_ranks()
    {
        double[] values = [38, 4, 10, 5];

        Assert.Equal(7.5, InsightMath.Percentile(values, 0.5));
        Assert.Equal(25.4, InsightMath.Round(InsightMath.Percentile(values, 0.85)));
        Assert.Equal(4, InsightMath.Percentile([4], 0.85));
        Assert.Null(InsightMath.Percentile([], 0.5));
    }

    [Fact]
    public void PerDay_buckets_by_local_date_and_fills_empty_days()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");
        var lateUtc = new DateTimeOffset(2026, 11, 24, 12, 30, 0, TimeSpan.Zero); // 25 Nov 01:30 in Auckland

        var utc = InsightMath.PerDay([lateUtc], new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 26), TimeZoneInfo.Utc);
        var auckland = InsightMath.PerDay([lateUtc], new DateOnly(2026, 11, 24), new DateOnly(2026, 11, 26), zone);

        Assert.Equal([1, 0, 0], utc.Select(d => d.Count));
        Assert.Equal([0, 1, 0], auckland.Select(d => d.Count));
    }

    [Fact]
    public void Segments_follow_moves_and_stop_at_completion()
    {
        var card = new SegmentCard(Done, DaysAgo(10), DaysAgo(5), DaysAgo(4), null);
        ListMove[] moves = [new(DaysAgo(8), Todo, Doing), new(DaysAgo(5), Doing, Done)];

        var segments = InsightMath.Segments(card, Todo, moves, Now);

        Assert.Equal([(Todo, 2.0), (Doing, 3.0), (Done, 1.0)], segments.Select(s => (s.ListId, s.Days)));
    }

    [Fact]
    public void Open_card_counts_up_to_now()
    {
        var card = new SegmentCard(Doing, DaysAgo(3), DaysAgo(3), null, null);

        var segment = Assert.Single(InsightMath.Segments(card, Doing, [], Now));

        Assert.Equal((Doing, 3.0), (segment.ListId, segment.Days));
    }

    [Fact]
    public void Missing_move_into_current_list_starts_the_stay_at_list_entered()
    {
        var card = new SegmentCard(Done, DaysAgo(10), DaysAgo(2), null, null);

        var segments = InsightMath.Segments(card, Todo, [new(DaysAgo(8), Todo, Doing)], Now);

        Assert.Equal([(Todo, 2.0), (Doing, 6.0), (Done, 2.0)], segments.Select(s => (s.ListId, s.Days)));
    }

    [Fact]
    public void Unknown_lists_are_dropped()
    {
        var card = new SegmentCard(Doing, DaysAgo(6), DaysAgo(1), null, null);

        var segments = InsightMath.Segments(card, null, [new(DaysAgo(1), null, Doing)], Now);

        Assert.Equal([(Doing, 1.0)], segments.Select(s => (s.ListId, s.Days)));
    }
}
