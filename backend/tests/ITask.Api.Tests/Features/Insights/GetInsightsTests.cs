using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Insights;
using ITask.Api.Domain;
using ITask.Api.Features.Insights.GetInsights;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Tests.Features.Insights;

public sealed class GetInsightsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Url(Guid boardId, string query = "") => $"/api/boards/{boardId}/insights{query}";

    private sealed record Seeded(HttpClient Owner, Guid BoardId, Guid[] Lists, Dictionary<string, Guid> Cards, DateTimeOffset Now);

    private sealed record CardSeed(
        string Title, int List, double CreatedDaysAgo, double? CompletedDaysAgo = null, double? ArchivedDaysAgo = null,
        double? EnteredDaysAgo = null);

    /// <summary>
    /// Board with lists To do, In progress (WIP 1), Review, Done and cards with known history (days ago):
    /// A: created 10 in To do (ids), → In progress 8, → Done 5, completed 5. Cycle 5.
    /// B: created 20 in To do (names only), → In progress 16, → Done 10, completed 10. Cycle 10.
    /// C: created 40, no activity, in Done, completed 2. Cycle 38.
    /// D: open, created 3 in In progress.
    /// E: open, created 6 in To do, → In progress 1.
    /// F: open in Done (final list), created 1.
    /// G: archived, never completed, created 4.
    /// H: archived, created 7 in Review, completed 3. Cycle 4.
    /// </summary>
    private async Task<Seeded> SeedAsync()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync("Insights");
        var userId = await owner.GetUserIdAsync();
        var lists = (await owner.GetListsAsync(boardId)).Select(l => l.Id).ToArray();
        var now = DateTimeOffset.UtcNow;
        DateTimeOffset Ago(double days) => now.AddDays(-days);

        CardSeed[] seeds =
        [
            new("A", 3, 10, 5, EnteredDaysAgo: 5),
            new("B", 3, 20, 10, EnteredDaysAgo: 10),
            new("C", 3, 40, 2, EnteredDaysAgo: 40),
            new("D", 1, 3),
            new("E", 1, 6, EnteredDaysAgo: 1),
            new("F", 3, 1),
            new("G", 0, 4, ArchivedDaysAgo: 2),
            new("H", 2, 7, 3, ArchivedDaysAgo: 1),
        ];
        var ids = new Dictionary<string, Guid>();

        await factory.WithDbAsync(async db =>
        {
            var cards = seeds.Select((s, i) => new Card
            {
                BoardId = boardId,
                ListId = lists[s.List],
                Number = 100 + i,
                Title = s.Title,
                Position = 1024 * (i + 1),
                CreatedById = userId,
            }).ToList();
            db.Cards.AddRange(cards);
            await db.SaveChangesAsync();

            // A second save: the first one stamps ListEnteredAt with the current time.
            foreach (var (card, seed) in cards.Zip(seeds))
            {
                card.CreatedAt = Ago(seed.CreatedDaysAgo);
                card.CompletedAt = seed.CompletedDaysAgo is { } c ? Ago(c) : null;
                card.ArchivedAt = seed.ArchivedDaysAgo is { } a ? Ago(a) : null;
                card.ListEnteredAt = Ago(seed.EnteredDaysAgo ?? seed.CreatedDaysAgo);
                ids[seed.Title] = card.Id;
            }

            var list = await db.Lists.FirstAsync(l => l.Id == lists[1]);
            list.WipLimit = 1;

            ActivityEntry Entry(string card, string type, double daysAgo, string data) => new()
            {
                BoardId = boardId,
                CardId = ids[card],
                ActorId = userId,
                Type = type,
                Data = data,
                CreatedAt = Ago(daysAgo),
            };

            string Created(int l) => $$"""{"list":"x","listId":"{{lists[l]}}"}""";
            string Moved(int from, int to) => $$"""{"fromList":"x","toList":"y","fromListId":"{{lists[from]}}","toListId":"{{lists[to]}}"}""";
            db.Activity.AddRange(
                Entry("A", ActivityTypes.CardCreated, 10, Created(0)),
                Entry("A", ActivityTypes.CardMoved, 8, Moved(0, 1)),
                Entry("A", ActivityTypes.CardMoved, 6, """{"list":"In progress"}"""),
                Entry("A", ActivityTypes.CardMoved, 5, Moved(1, 3)),
                Entry("B", ActivityTypes.CardCreated, 20, """{"list":"To do"}"""),
                Entry("B", ActivityTypes.CardMoved, 16, """{"fromList":"To do","toList":"In progress"}"""),
                Entry("B", ActivityTypes.CardMoved, 10, """{"fromList":"In progress","toList":"done"}"""),
                Entry("D", ActivityTypes.CardCreated, 3, Created(1)),
                Entry("E", ActivityTypes.CardCreated, 6, Created(0)),
                Entry("E", ActivityTypes.CardMoved, 1, Moved(0, 1)),
                Entry("F", ActivityTypes.CardCreated, 1, Created(3)),
                Entry("G", ActivityTypes.CardCreated, 4, Created(0)),
                Entry("H", ActivityTypes.CardCreated, 7, Created(2)));
            await db.SaveChangesAsync();
        });

        return new Seeded(owner, boardId, lists, ids, now);
    }

    private static async Task<GetInsights.Response> GetAsync(HttpClient client, Guid boardId, string query = "")
    {
        var response = await client.GetAsync(new Uri(Url(boardId, query), UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GetInsights.Response>())!;
    }

    [Fact]
    public async Task Numbers_match_the_hand_checked_seed()
    {
        var s = await SeedAsync();

        var result = await GetAsync(s.Owner, s.BoardId);

        Assert.Equal(30, result.Days);
        Assert.Equal(30, result.Throughput.Count);
        Assert.Equal(DateOnly.FromDateTime(s.Now.UtcDateTime), result.To);
        Assert.Equal(result.To.AddDays(-29), result.From);

        // Done: A, B, C, H (H archived but completed in range). Created: A, B, D, E, F, H (C too old, G archived).
        Assert.Equal(4, result.Throughput.Sum(d => d.Count));
        Assert.Equal(1, result.Throughput.Single(d => d.Date == DateOnly.FromDateTime(s.Now.AddDays(-2).UtcDateTime)).Count);
        Assert.Equal(6, result.Created.Sum(d => d.Count));

        // Cycle times 4, 5, 10, 38.
        Assert.Equal(new GetInsights.CycleTime(4, 7.5, 25.4), result.CycleTime);

        // To do: A 2, B 4, E 5. In progress: A 3, B 6, D 3, E 1. Review: H 4. Done: C 38, F 1 (A, B stop at completion).
        Assert.Equal(
            [(4.0, 3), (3.0, 4), (4.0, 1), (19.5, 2)],
            result.TimeInList.Select(l => (l.MedianDays!.Value, l.Samples)));

        // Open cards outside the final list, longest in their list first.
        Assert.Equal(["D", "E"], result.Aging.Select(a => a.Title));
        Assert.Equal([3.0, 1.0], result.Aging.Select(a => a.Days));
        Assert.Equal("INS-103", result.Aging[0].Key);

        Assert.Equal([(0, (int?)null), (2, 1), (0, null), (1, null)], result.Wip.Select(w => (w.Open, w.Limit)));
    }

    [Fact]
    public async Task Days_are_bucketed_in_the_requested_time_zone()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync("Zones");
        var userId = await owner.GetUserIdAsync();
        var list = (await owner.GetListsAsync(boardId))[0].Id;
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

        // 12:30 UTC yesterday is already the next calendar day in Auckland.
        var utcYesterday = DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(-1);
        var completed = new DateTimeOffset(utcYesterday.AddHours(12.5), TimeSpan.Zero);
        await factory.WithDbAsync(async db =>
        {
            var card = new Card
            {
                BoardId = boardId, ListId = list, Number = 1, Title = "Late", CreatedById = userId,
                CreatedAt = completed.AddDays(-1), CompletedAt = completed,
            };
            db.Cards.Add(card);
            await db.SaveChangesAsync();
        });

        var utc = await GetAsync(owner, boardId);
        var auckland = await GetAsync(owner, boardId, "?tz=Pacific/Auckland");

        Assert.Equal(DateOnly.FromDateTime(utcYesterday), utc.Throughput.Single(d => d.Count > 0).Date);
        Assert.Equal(InsightMath.LocalDate(completed, zone), auckland.Throughput.Single(d => d.Count > 0).Date);
        Assert.NotEqual(utc.Throughput.Single(d => d.Count > 0).Date, auckland.Throughput.Single(d => d.Count > 0).Date);
    }

    [Fact]
    public async Task Range_switch_widens_the_window()
    {
        var s = await SeedAsync();

        var result = await GetAsync(s.Owner, s.BoardId, "?days=60");

        Assert.Equal(60, result.Throughput.Count);
        Assert.Equal(7, result.Created.Sum(d => d.Count)); // C (40 days ago) now counts.
    }

    [Fact]
    public async Task Moves_through_the_api_record_list_ids()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync("Ids");
        var lists = await owner.GetListsAsync(boardId);
        var card = await owner.CreateCardAsync(boardId, lists[0].Id, "Move me");
        (await owner.MoveCardAsync(boardId, card, lists[1].Id)).EnsureSuccessStatusCode();

        var data = new List<string>();
        await factory.WithDbAsync(async db => data.AddRange(await db.Activity
            .Where(a => a.CardId == card.Id).OrderBy(a => a.Type).Select(a => a.Data).ToListAsync()));

        Assert.Contains($"\"listId\":\"{lists[0].Id}\"", data[0], StringComparison.Ordinal);
        Assert.Contains($"\"toListId\":\"{lists[1].Id}\"", data[1], StringComparison.Ordinal);

        var result = await GetAsync(owner, boardId);
        Assert.Equal([1, 1, 0, 0], result.TimeInList.Select(l => l.Samples));
    }

    [Fact]
    public async Task Empty_board_returns_zeroes()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync("Empty");

        var result = await GetAsync(owner, boardId);

        Assert.Equal(0, result.Throughput.Sum(d => d.Count));
        Assert.Equal(new GetInsights.CycleTime(0, null, null), result.CycleTime);
        Assert.All(result.TimeInList, l => Assert.Null(l.MedianDays));
        Assert.Empty(result.Aging);
    }

    [Theory]
    [InlineData("?days=45")]
    [InlineData("?days=120")]
    [InlineData("?tz=Mars/Olympus")]
    public async Task Invalid_range_or_zone_is_400(string query)
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();

        var response = await owner.GetAsync(new Uri(Url(boardId, query), UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Viewer_reads_and_non_member_gets_404()
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var viewer = await board.Viewer.GetAsync(new Uri(Url(board.BoardId), UriKind.Relative));
        var stranger = await board.Stranger.GetAsync(new Uri(Url(board.BoardId), UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, viewer.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
    }
}
