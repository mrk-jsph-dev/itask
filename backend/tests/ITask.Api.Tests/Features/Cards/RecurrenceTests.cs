using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Domain;
using ITask.Api.Features.Cards.GetCard;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Cards;

public sealed class RecurrenceTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly int[] MonWedWithRepeat = [3, 1, 1];

    private static readonly int[] Tuesday = [2];

    private static readonly int[] EveryDay = [0, 1, 2, 3, 4, 5, 6];

    private static async Task<RecurrenceView> SetAsync(HttpClient client, CardOnBoard card, object body)
    {
        var response = await client.PutAsJsonAsync($"{card.Url}/recurrence", body);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecurrenceView>())!;
    }

    private static async Task<Guid> ListAsync(CardOnBoard card, int index) =>
        (await card.Board.Owner.GetListsAsync(card.BoardId))[index].Id;

    [Fact]
    public async Task Set_returns_the_rule_and_the_card_shows_it()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var todo = await ListAsync(card, 0);

        var rule = await SetAsync(card.Board.Editor, card, new { kind = "Weekly", weekdays = MonWedWithRepeat, targetListId = todo });
        var detail = await card.Board.Viewer.GetFromJsonAsync<GetCard.Response>(card.Url);

        Assert.Equal(RecurrenceKind.Weekly, rule.Kind);
        Assert.Equal([1, 3], rule.Weekdays);
        Assert.True(rule.NextRunAt > DateTimeOffset.UtcNow);
        Assert.Equal(rule.NextRunAt, detail!.Recurrence!.NextRunAt);
        Assert.True((await card.SummaryAsync()).Recurring);
    }

    [Fact]
    public async Task Changing_the_rule_replaces_it_and_clearing_removes_it()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var todo = await ListAsync(card, 0);
        await SetAsync(card.Board.Owner, card, new { kind = "Daily", targetListId = todo });

        var monthly = await SetAsync(card.Board.Owner, card, new { kind = "Monthly", dayOfMonth = 31, weekdays = Tuesday, targetListId = todo });
        Assert.Equal((RecurrenceKind.Monthly, 31, 0), (monthly.Kind, monthly.DayOfMonth, monthly.Weekdays.Count));

        var cleared = await card.Board.Editor.DeleteAsync(new Uri($"{card.Url}/recurrence", UriKind.Relative));
        var again = await card.Board.Editor.DeleteAsync(new Uri($"{card.Url}/recurrence", UriKind.Relative));

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent), (cleared.StatusCode, again.StatusCode));
        Assert.Null((await card.Board.Owner.GetFromJsonAsync<GetCard.Response>(card.Url))!.Recurrence);
        Assert.False((await card.SummaryAsync()).Recurring);
    }

    [Fact]
    public async Task Set_validates_the_rule_and_the_list()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var todo = await ListAsync(card, 0);
        var other = await CardOnBoard.CreateAsync(factory);
        var foreignList = await ListAsync(other, 0);
        object[] bodies =
        [
            new { kind = "Weekly", weekdays = Array.Empty<int>(), targetListId = todo },
            new { kind = "Weekly", weekdays = new[] { 7 }, targetListId = todo },
            new { kind = "Monthly", targetListId = todo },
            new { kind = "Monthly", dayOfMonth = 32, targetListId = todo },
            new { kind = "Daily", targetListId = foreignList },
        ];

        foreach (var body in bodies)
        {
            var response = await card.Board.Owner.PutAsJsonAsync($"{card.Url}/recurrence", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_editors_may_set_or_clear(string role, HttpStatusCode expected)
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var todo = await ListAsync(card, 0);
        var client = card.Board.For(role);

        var set = await client.PutAsJsonAsync($"{card.Url}/recurrence", new { kind = "Daily", targetListId = todo });
        var clear = await client.DeleteAsync(new Uri($"{card.Url}/recurrence", UriKind.Relative));

        Assert.Equal((expected, expected), (set.StatusCode, clear.StatusCode));
    }

    [Fact]
    public async Task Archived_cards_cannot_get_a_rule()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var todo = await ListAsync(card, 0);
        await card.Board.Owner.PostEmptyAsync($"{card.Url}/archive");

        var response = await card.Board.Owner.PutAsJsonAsync($"{card.Url}/recurrence", new { kind = "Daily", targetListId = todo });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Completing_creates_the_next_copy_in_the_target_list_and_moves_the_rule()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var owner = card.Board.Owner;
        var lists = await owner.GetListsAsync(card.BoardId);
        var inProgress = lists[1].Id;
        var due = new DateTimeOffset(2026, 1, 5, 17, 30, 0, TimeSpan.Zero);
        (await owner.PatchAsJsonAsync(card.Url, new { dueAt = due, startAt = due.AddDays(-1), priority = "High", description = "Notes" })).EnsureSuccessStatusCode();
        var label = await owner.PostAsJsonAsync($"/api/boards/{card.BoardId}/labels", new { name = "Ops", color = "#0F766E" });
        var labelId = (await label.Content.ReadFromJsonAsync<LabelChip>())!.Id;
        (await owner.PutAsJsonAsync($"{card.Url}/labels", new { labelIds = new[] { labelId } })).EnsureSuccessStatusCode();
        await card.AddItemAsync(owner, "Step one");
        var ticked = await card.AddItemAsync(owner, "Step two");
        (await owner.PutAsJsonAsync($"{card.Url}/checklist/{ticked.Id}/done", new { isDone = true })).EnsureSuccessStatusCode();
        var rule = await SetAsync(owner, card, new { kind = "Weekly", weekdays = EveryDay, targetListId = inProgress });

        (await owner.PostEmptyAsync($"{card.Url}/complete")).EnsureSuccessStatusCode();

        var all = (await owner.GetListsAsync(card.BoardId)).SelectMany(l => l.Cards).ToList();
        var copy = Assert.Single(all, c => c.Id != card.CardId);
        var copyDetail = (await owner.GetFromJsonAsync<GetCard.Response>($"{CardTestKit.Cards(card.BoardId)}/{copy.Id}"))!;
        var expectedDue = new DateTimeOffset(DateOnly.FromDateTime(rule.NextRunAt.UtcDateTime).ToDateTime(new TimeOnly(17, 30)), TimeSpan.Zero);

        Assert.Equal((inProgress, "Write spec", CardPriority.High, 2), (copy.ListId, copy.Title, copy.Priority, copy.Number));
        Assert.Equal((expectedDue, expectedDue.AddDays(-1)), (copy.DueAt, copy.StartAt));
        Assert.Equal(["Ops"], copy.Labels.Select(l => l.Name));
        Assert.Equal(["Step one", "Step two"], copyDetail.Checklist.Select(i => i.Text));
        Assert.All(copyDetail.Checklist, i => Assert.False(i.IsDone));
        Assert.Equal("Notes", copyDetail.Description);
        Assert.True(copy.Recurring);
        Assert.False(all.Single(c => c.Id == card.CardId).Recurring);
        Assert.Equal(rule.NextRunAt.AddDays(1), copyDetail.Recurrence!.NextRunAt);

        // Reopening and completing the old card again doesn't make another copy.
        await owner.PostEmptyAsync($"{card.Url}/reopen");
        await owner.PostEmptyAsync($"{card.Url}/complete");
        Assert.Equal(2, (await owner.GetListsAsync(card.BoardId)).SelectMany(l => l.Cards).Count());
    }
}
