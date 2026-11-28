using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Domain;
using ITask.Api.Features.Cards.GetCard;
using ITask.Api.Features.Cards.QueryCards;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static ITask.Api.Tests.Infrastructure.AutomationTestKit;

namespace ITask.Api.Tests.Features.Cards;

public sealed class CardDependencyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>A board with members, its default lists and two cards in To do: <c>A</c> will wait on <c>B</c>.</summary>
    private sealed record Setup(BoardTestKit.BoardWithMembers Board, List<Guid> Lists, CardSummary A, CardSummary B)
    {
        public Guid BoardId => Board.BoardId;

        public HttpClient Owner => Board.Owner;

        public string Url(Guid cardId) => $"{CardTestKit.Cards(BoardId)}/{cardId}";

        public Task<HttpResponseMessage> BlockAsync(Guid cardId, Guid blockerId, HttpClient? client = null) =>
            (client ?? Owner).PutAsync(new Uri($"{Url(cardId)}/blocked-by/{blockerId}", UriKind.Relative), null);

        public Task<HttpResponseMessage> UnblockAsync(Guid cardId, Guid blockerId, HttpClient? client = null) =>
            (client ?? Owner).DeleteAsync(new Uri($"{Url(cardId)}/blocked-by/{blockerId}", UriKind.Relative));

        public async Task<CardSummary> SummaryAsync(Guid cardId) =>
            (await Owner.GetListsAsync(BoardId)).SelectMany(l => l.Cards).Single(c => c.Id == cardId);

        public async Task<CardSummary> NewCardAsync(string title) => await Owner.CreateCardAsync(BoardId, Lists[0], title);
    }

    private async Task<Setup> SetupAsync()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var lists = (await board.Owner.GetListsAsync(board.BoardId)).Select(l => l.Id).ToList();
        var a = await board.Owner.CreateCardAsync(board.BoardId, lists[0], "Ship release");
        var b = await board.Owner.CreateCardAsync(board.BoardId, lists[0], "Write tests");
        return new Setup(board, lists, a, b);
    }

    private static async Task<CardSummary> ReadAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardSummary>())!;
    }

    [Fact]
    public async Task Editor_adds_a_blocker_and_both_tiles_show_it()
    {
        var s = await SetupAsync();

        var a = await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id, s.Board.Editor));

        Assert.True(a.Blocked);
        Assert.Equal(1, a.BlockerCount);
        var b = await s.SummaryAsync(s.B.Id);
        Assert.False(b.Blocked);
        Assert.Equal(1, b.BlockingCount);

        var detail = (await s.Board.Viewer.GetFromJsonAsync<GetCard.Response>(s.Url(s.A.Id)))!;
        var blocker = Assert.Single(detail.BlockedBy);
        Assert.Equal((s.B.Id, s.B.Key, "Write tests", false), (blocker.Id, blocker.Key, blocker.Title, blocker.Completed));
    }

    [Fact]
    public async Task Adding_and_removing_are_idempotent()
    {
        var s = await SetupAsync();
        await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));
        var before = await factory.ActivityTypesAsync(s.BoardId);

        var again = await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));

        Assert.Equal(1, again.BlockerCount);
        Assert.Equal(before, await factory.ActivityTypesAsync(s.BoardId));

        var removed = await ReadAsync(await s.UnblockAsync(s.A.Id, s.B.Id));
        Assert.False(removed.Blocked);
        await ReadAsync(await s.UnblockAsync(s.A.Id, s.B.Id));
        Assert.Single(await factory.ActivityTypesAsync(s.BoardId), t => t == "card.dependency_removed");
    }

    [Fact]
    public async Task Viewers_get_403_and_strangers_404()
    {
        var s = await SetupAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await s.BlockAsync(s.A.Id, s.B.Id, s.Board.Viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await s.UnblockAsync(s.A.Id, s.B.Id, s.Board.Viewer)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.BlockAsync(s.A.Id, s.B.Id, s.Board.Stranger)).StatusCode);
    }

    [Fact]
    public async Task A_card_from_another_board_is_not_found()
    {
        var s = await SetupAsync();
        var otherBoard = await s.Owner.CreateBoardAsync("Other");
        var otherList = (await s.Owner.GetListsAsync(otherBoard))[0].Id;
        var foreign = await s.Owner.CreateCardAsync(otherBoard, otherList, "Elsewhere");

        Assert.Equal(HttpStatusCode.NotFound, (await s.BlockAsync(s.A.Id, foreign.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.BlockAsync(foreign.Id, s.A.Id)).StatusCode);
    }

    [Fact]
    public async Task Self_links_archived_blockers_and_loops_are_refused()
    {
        var s = await SetupAsync();
        var c = await s.NewCardAsync("Plan launch");

        Assert.Equal(HttpStatusCode.BadRequest, (await s.BlockAsync(s.A.Id, s.A.Id)).StatusCode);

        // A waits on B, B waits on C: C waiting on A would close the loop.
        await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));
        await ReadAsync(await s.BlockAsync(s.B.Id, c.Id));
        var loop = await s.BlockAsync(c.Id, s.A.Id);
        Assert.Equal(HttpStatusCode.Conflict, loop.StatusCode);
        var problem = await loop.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("dependency_cycle", problem.GetProperty("code").GetString());

        var archived = await s.NewCardAsync("Old idea");
        (await s.Owner.PostEmptyAsync($"{s.Url(archived.Id)}/archive")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await s.BlockAsync(s.A.Id, archived.Id)).StatusCode);
    }

    [Fact]
    public async Task A_card_waits_on_at_most_ten_cards()
    {
        var s = await SetupAsync();
        for (var i = 0; i < CardDependency.MaxBlockersPerCard; i++)
        {
            var blocker = await s.NewCardAsync($"Step {i}");
            await ReadAsync(await s.BlockAsync(s.A.Id, blocker.Id));
        }

        var extra = await s.NewCardAsync("One more");

        Assert.Equal(HttpStatusCode.Conflict, (await s.BlockAsync(s.A.Id, extra.Id)).StatusCode);
    }

    [Fact]
    public async Task Completing_the_last_blocker_unblocks_and_notifies_the_assignees()
    {
        var s = await SetupAsync();
        var c = await s.NewCardAsync("Review copy");
        await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));
        await ReadAsync(await s.BlockAsync(s.A.Id, c.Id));
        var editorId = await s.Board.Editor.GetUserIdAsync();
        var ownerId = await s.Owner.GetUserIdAsync();
        (await s.Owner.PutAsJsonAsync($"{s.Url(s.A.Id)}/assignees", new { userIds = new[] { editorId, ownerId } })).EnsureSuccessStatusCode();

        (await s.Owner.PostEmptyAsync($"{s.Url(s.B.Id)}/complete")).EnsureSuccessStatusCode();
        Assert.True((await s.SummaryAsync(s.A.Id)).Blocked);
        Assert.DoesNotContain("card.unblocked", await factory.ActivityTypesAsync(s.BoardId));

        // Archiving counts as finishing it too.
        (await s.Owner.PostEmptyAsync($"{s.Url(c.Id)}/archive")).EnsureSuccessStatusCode();

        Assert.False((await s.SummaryAsync(s.A.Id)).Blocked);
        Assert.Single(await factory.ActivityTypesAsync(s.BoardId), t => t == "card.unblocked");
        var note = Assert.Single(await s.Board.Editor.NotificationsAsync(), n => n.Type == "unblocked");
        Assert.Equal(s.A.Id, note.CardId);
        Assert.Equal(s.A.Key, note.Data.GetProperty("card").GetProperty("key").GetString());
        Assert.Equal("Review copy", note.Data.GetProperty("blocker").GetProperty("title").GetString());
        Assert.DoesNotContain(await s.Owner.NotificationsAsync(), n => n.Type == "unblocked");
    }

    [Fact]
    public async Task Completing_a_blocker_of_a_finished_card_records_nothing()
    {
        var s = await SetupAsync();
        await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));
        (await s.Owner.PostEmptyAsync($"{s.Url(s.A.Id)}/complete")).EnsureSuccessStatusCode();

        (await s.Owner.PostEmptyAsync($"{s.Url(s.B.Id)}/complete")).EnsureSuccessStatusCode();

        Assert.DoesNotContain("card.unblocked", await factory.ActivityTypesAsync(s.BoardId));
    }

    [Fact]
    public async Task A_not_blocked_doorway_refuses_blocked_cards()
    {
        var s = await SetupAsync();
        var done = s.Lists[3];
        await s.Owner.CreateAutomationAsync(s.BoardId, Doorway(done, "Enter", "Block", [new { type = "blocked.no" }]));
        await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));

        var refused = await s.Owner.MoveCardAsync(s.BoardId, await s.SummaryAsync(s.A.Id), done);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("doorway", problem.GetProperty("code").GetString());
        Assert.Equal("no open blockers", problem.GetProperty("unmet")[0].GetString());

        (await s.Owner.PostEmptyAsync($"{s.Url(s.B.Id)}/complete")).EnsureSuccessStatusCode();
        (await s.Owner.MoveCardAsync(s.BoardId, await s.SummaryAsync(s.A.Id), done)).EnsureSuccessStatusCode();
    }

    [Theory]
    [InlineData("Warn")]
    [InlineData("Fix")]
    public async Task Warn_and_fix_doorways_let_blocked_cards_through(string mode)
    {
        var s = await SetupAsync();
        var done = s.Lists[3];
        await s.Owner.CreateAutomationAsync(s.BoardId, Doorway(done, "Enter", mode, [new { type = "blocked.no" }]));
        await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));

        (await s.Owner.MoveCardAsync(s.BoardId, await s.SummaryAsync(s.A.Id), done)).EnsureSuccessStatusCode();

        Assert.True((await s.SummaryAsync(s.A.Id)).Blocked);
    }

    [Fact]
    public async Task Rules_run_when_a_card_is_unblocked_and_can_check_blocked_state()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId,
            Rule(new { type = "card.unblocked" }, [new { type = "priority", priority = "High" }]));
        await s.Owner.CreateAutomationAsync(s.BoardId,
            Rule(new { type = "comment.added" }, [new { type = "label.add", labelId = await LabelAsync(s, "Waiting") }],
                [new { type = "blocked.yes" }]));
        await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));

        (await s.Owner.PostAsJsonAsync($"{s.Url(s.A.Id)}/comments", new { body = "Any news?" })).EnsureSuccessStatusCode();
        (await s.Owner.PostEmptyAsync($"{s.Url(s.B.Id)}/complete")).EnsureSuccessStatusCode();

        var a = await s.SummaryAsync(s.A.Id);
        Assert.Equal(CardPriority.High, a.Priority);
        Assert.Equal("Waiting", Assert.Single(a.Labels).Name);
    }

    [Fact]
    public async Task An_automation_that_completes_a_blocker_unblocks_too()
    {
        var s = await SetupAsync();
        await s.Owner.CreateAutomationAsync(s.BoardId, Rule(new { type = "card.entered", listId = s.Lists[3] }, [new { type = "complete" }]));
        await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));

        (await s.Owner.MoveCardAsync(s.BoardId, await s.SummaryAsync(s.B.Id), s.Lists[3])).EnsureSuccessStatusCode();

        Assert.False((await s.SummaryAsync(s.A.Id)).Blocked);
        Assert.Contains("card.unblocked", await factory.ActivityTypesAsync(s.BoardId));
    }

    [Fact]
    public async Task The_cards_query_filters_on_blocked()
    {
        var s = await SetupAsync();
        await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));

        async Task<string[]> Titles(string query) =>
            [.. (await s.Owner.GetFromJsonAsync<QueryCards.Response>($"{CardTestKit.Cards(s.BoardId)}?{query}"))!.Items.Select(c => c.Title)];

        Assert.Equal(["Ship release"], await Titles("blocked=yes"));
        Assert.Equal(["Write tests"], await Titles("blocked=no"));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await s.Owner.GetAsync(new Uri($"{CardTestKit.Cards(s.BoardId)}?blocked=maybe", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_board_removes_its_dependencies()
    {
        var s = await SetupAsync();
        await ReadAsync(await s.BlockAsync(s.A.Id, s.B.Id));
        (await s.Owner.PostEmptyAsync($"/api/boards/{s.BoardId}/archive")).EnsureSuccessStatusCode();

        (await s.Owner.DeleteAsync(new Uri($"/api/boards/{s.BoardId}", UriKind.Relative))).EnsureSuccessStatusCode();

        await factory.WithDbAsync(async db =>
            Assert.False(await db.CardDependencies.AnyAsync(d => d.CardId == s.A.Id || d.BlockedById == s.B.Id)));
    }

    private static async Task<Guid> LabelAsync(Setup s, string name)
    {
        var response = await s.Owner.PostAsJsonAsync($"/api/boards/{s.BoardId}/labels", new { name, color = "#E11D48" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }
}
