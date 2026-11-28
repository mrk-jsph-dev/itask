using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Features.Cards.QueryCards;
using ITask.Api.Features.Cards.SetCardAssignees;
using ITask.Api.Features.Cards.SetCardLabels;
using ITask.Api.Features.Labels.CreateLabel;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Cards;

public sealed class QueryCardsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Board(HttpClient Client, Guid BoardId, Guid TodoId, Guid DoingId);

    private async Task<Board> BoardAsync()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync("Roadmap");
        var lists = await client.GetListsAsync(boardId);
        return new Board(client, boardId, lists[0].Id, lists[1].Id);
    }

    private static async Task<QueryCards.Response> QueryAsync(Board board, string query = "")
    {
        var response = await board.Client.GetAsync(new Uri($"{CardTestKit.Cards(board.BoardId)}?{query}", UriKind.Relative));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<QueryCards.Response>())!;
    }

    private static async Task<string[]> TitlesAsync(Board board, string query = "") =>
        [.. (await QueryAsync(board, query)).Items.Select(c => c.Title)];

    private static async Task PatchAsync(Board board, CardSummary card, object body) =>
        (await board.Client.PatchAsJsonAsync($"{CardTestKit.Cards(board.BoardId)}/{card.Id}", body)).EnsureSuccessStatusCode();

    private static async Task<Guid> LabelAsync(Board board, string name)
    {
        var response = await board.Client.PostAsJsonAsync(
            $"/api/boards/{board.BoardId}/labels", new CreateLabel.Request(name, "#1D5BF5"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LabelChip>())!.Id;
    }

    private static async Task SetLabelsAsync(Board board, CardSummary card, params Guid[] ids) =>
        (await board.Client.PutAsJsonAsync(
            $"{CardTestKit.Cards(board.BoardId)}/{card.Id}/labels", new SetCardLabels.Request(ids))).EnsureSuccessStatusCode();

    [Fact]
    public async Task Returns_active_cards_in_board_order_without_archived_ones()
    {
        var board = await BoardAsync();
        await board.Client.CreateCardAsync(board.BoardId, board.DoingId, "Second list");
        await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "First");
        var gone = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Archived");
        await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Third");
        (await board.Client.PostEmptyAsync($"{CardTestKit.Cards(board.BoardId)}/{gone.Id}/archive")).EnsureSuccessStatusCode();

        var result = await QueryAsync(board);

        Assert.Equal(["First", "Third", "Second list"], result.Items.Select(c => c.Title));
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Cards_on_archived_lists_are_left_out()
    {
        var board = await BoardAsync();
        await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Keep");
        await board.Client.CreateCardAsync(board.BoardId, board.DoingId, "Hidden");
        (await board.Client.PostEmptyAsync($"/api/boards/{board.BoardId}/lists/{board.DoingId}/archive"))
            .EnsureSuccessStatusCode();

        Assert.Equal(["Keep"], await TitlesAsync(board));
    }

    [Fact]
    public async Task Labels_match_any_and_combine_with_other_filters()
    {
        var board = await BoardAsync();
        var bug = await LabelAsync(board, "Bug");
        var ui = await LabelAsync(board, "UI");
        var a = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Bug card");
        var b = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "UI card");
        await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Plain");
        await SetLabelsAsync(board, a, bug);
        await SetLabelsAsync(board, b, ui);
        await PatchAsync(board, b, new { priority = "High" });

        Assert.Equal(["Bug card", "UI card"], await TitlesAsync(board, $"labels={bug},{ui}"));
        Assert.Equal(["UI card"], await TitlesAsync(board, $"labels={bug},{ui}&priority=High,Urgent"));
        Assert.Equal(["Bug card", "Plain"], await TitlesAsync(board, "priority=None"));
    }

    [Fact]
    public async Task Assignees_match_ids_or_unassigned()
    {
        var board = await BoardAsync();
        var me = await board.Client.GetUserIdAsync();
        var mine = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Mine");
        await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Nobody");
        (await board.Client.PutAsJsonAsync(
                $"{CardTestKit.Cards(board.BoardId)}/{mine.Id}/assignees", new SetCardAssignees.Request([me])))
            .EnsureSuccessStatusCode();

        Assert.Equal(["Mine"], await TitlesAsync(board, $"assignees={me}"));
        Assert.Equal(["Nobody"], await TitlesAsync(board, "assignees=none"));
        Assert.Equal(["Mine", "Nobody"], await TitlesAsync(board, $"assignees={me},none"));
    }

    [Fact]
    public async Task Text_matches_title_or_card_key_and_completed_can_be_hidden()
    {
        var board = await BoardAsync();
        var first = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Write the Launch post");
        var done = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Launch party");
        await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Other");
        (await board.Client.PostEmptyAsync($"{CardTestKit.Cards(board.BoardId)}/{done.Id}/complete")).EnsureSuccessStatusCode();

        Assert.Equal(["Write the Launch post", "Launch party"], await TitlesAsync(board, "q=launch"));
        Assert.Equal(["Write the Launch post"], await TitlesAsync(board, "q=launch&completed=false"));
        Assert.Equal(["Write the Launch post"], await TitlesAsync(board, $"q={first.Key.ToLowerInvariant()}"));
        Assert.Empty(await TitlesAsync(board, "q=ZZZ-1"));
    }

    [Fact]
    public async Task Due_buckets_and_calendar_range()
    {
        var board = await BoardAsync();
        var now = DateTimeOffset.UtcNow;
        var late = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Late");
        var lateDone = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Late but done");
        var later = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Next month");
        await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "No date");
        await PatchAsync(board, late, new { dueAt = now.AddDays(-2) });
        await PatchAsync(board, lateDone, new { dueAt = now.AddDays(-2) });
        await PatchAsync(board, later, new { dueAt = now.AddDays(30) });
        (await board.Client.PostEmptyAsync($"{CardTestKit.Cards(board.BoardId)}/{lateDone.Id}/complete"))
            .EnsureSuccessStatusCode();
        var from = Uri.EscapeDataString(now.AddDays(20).ToString("O"));
        var to = Uri.EscapeDataString(now.AddDays(40).ToString("O"));

        Assert.Equal(["Late"], await TitlesAsync(board, "due=overdue&tz=Asia/Manila"));
        Assert.Equal(["Late", "No date"], await TitlesAsync(board, "due=overdue,none"));
        Assert.Empty(await TitlesAsync(board, "due=week"));
        Assert.Equal(["Next month"], await TitlesAsync(board, $"dueFrom={from}&dueTo={to}"));
    }

    [Fact]
    public async Task Window_keeps_cards_whose_date_range_overlaps_it()
    {
        var board = await BoardAsync();
        var from = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddDays(31);
        async Task Card(string title, DateTimeOffset? start, DateTimeOffset? due)
        {
            var card = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, title);
            if (start is not null || due is not null)
            {
                await PatchAsync(board, card, new { startAt = start, dueAt = due });
            }
        }

        await Card("Inside", from.AddDays(3), from.AddDays(5));
        await Card("Starts before", from.AddDays(-10), from.AddDays(2));
        await Card("Ends after", to.AddDays(-1), to.AddDays(10));
        await Card("Spans", from.AddDays(-5), to.AddDays(5));
        await Card("Due only", null, from.AddDays(9));
        await Card("Start only", from.AddDays(12), null);
        await Card("Before", from.AddDays(-9), from.AddDays(-1));
        await Card("After", to, to.AddDays(2));
        await Card("Due only outside", null, to.AddDays(1));
        await Card("No dates", null, null);
        var window = $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";

        Assert.Equal(
            ["Inside", "Starts before", "Ends after", "Spans", "Due only", "Start only"],
            await TitlesAsync(board, window));
    }

    [Fact]
    public async Task Window_returns_dependency_links_between_returned_cards_only()
    {
        var board = await BoardAsync();
        var due = DateTimeOffset.UtcNow.AddDays(2);
        var first = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "First");
        var second = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Second");
        var undated = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Undated");
        await PatchAsync(board, first, new { dueAt = due });
        await PatchAsync(board, second, new { dueAt = due.AddDays(1) });
        foreach (var (card, blocker) in new[] { (second, first), (first, undated) })
        {
            (await board.Client.PutAsync(
                new Uri($"{CardTestKit.Cards(board.BoardId)}/{card.Id}/blocked-by/{blocker.Id}", UriKind.Relative), null))
                .EnsureSuccessStatusCode();
        }

        var window = $"from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}"
            + $"&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(30).ToString("O"))}";
        var windowed = await QueryAsync(board, window);
        var plain = await QueryAsync(board);

        Assert.Equal([new QueryCards.DependencyLink(second.Id, first.Id)], windowed.Dependencies);
        Assert.Empty(plain.Dependencies);
    }

    [Fact]
    public async Task Sorts_by_due_priority_created_and_title()
    {
        var board = await BoardAsync();
        var now = DateTimeOffset.UtcNow;
        var bravo = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "bravo");
        var alpha = await board.Client.CreateCardAsync(board.BoardId, board.DoingId, "Alpha");
        var charlie = await board.Client.CreateCardAsync(board.BoardId, board.TodoId, "Charlie");
        await PatchAsync(board, bravo, new { dueAt = now.AddDays(3), priority = "Low" });
        await PatchAsync(board, alpha, new { dueAt = now.AddDays(1), priority = "Urgent" });
        await PatchAsync(board, charlie, new { priority = "Medium" });

        Assert.Equal(["bravo", "Charlie", "Alpha"], await TitlesAsync(board, "sort=position"));
        Assert.Equal(["Alpha", "bravo", "Charlie"], await TitlesAsync(board, "sort=due"));
        Assert.Equal(["Alpha", "Charlie", "bravo"], await TitlesAsync(board, "sort=priority"));
        Assert.Equal(["Charlie", "Alpha", "bravo"], await TitlesAsync(board, "sort=created"));
        Assert.Equal(["Alpha", "bravo", "Charlie"], await TitlesAsync(board, "sort=title"));
    }

    [Theory]
    [InlineData("labels=nope")]
    [InlineData("assignees=someone")]
    [InlineData("due=soon")]
    [InlineData("priority=Huge")]
    [InlineData("priority=3")]
    [InlineData("sort=random")]
    [InlineData("tz=Mars/Olympus")]
    [InlineData("dueFrom=2026-12-01T00:00:00Z&dueTo=2026-11-01T00:00:00Z")]
    [InlineData("dueFrom=2026-01-01T00:00:00Z&dueTo=2026-06-01T00:00:00Z")]
    [InlineData("from=2026-01-01T00:00:00Z")]
    [InlineData("to=2026-01-01T00:00:00Z")]
    [InlineData("from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
    [InlineData("from=2026-01-01T00:00:00Z&to=2026-07-01T00:00:00Z")]
    public async Task Invalid_parameters_are_rejected(string query)
    {
        var board = await BoardAsync();

        var response = await board.Client.GetAsync(new Uri($"{CardTestKit.Cards(board.BoardId)}?{query}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Viewer", HttpStatusCode.OK)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Members_read_and_strangers_get_404(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var response = await board.For(role).GetAsync(new Uri(CardTestKit.Cards(board.BoardId), UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }
}
