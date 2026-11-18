using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Domain;
using ITask.Api.Features.Boards.GetBoard;
using ITask.Api.Features.Boards.UpdateBoard;
using ITask.Api.Features.Cards.CreateCard;
using ITask.Api.Features.Cards.GetCard;
using ITask.Api.Features.Cards.SetCardAssignees;
using ITask.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ITask.Api.Tests.Features.Cards;

public sealed class CardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Card(Guid boardId, Guid cardId) => $"{CardTestKit.Cards(boardId)}/{cardId}";

    private async Task<(HttpClient Client, Guid BoardId, Guid TodoId)> BoardAsync(string name = "Roadmap")
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync(name);
        var todo = (await client.GetListsAsync(boardId))[0].Id;
        return (client, boardId, todo);
    }

    [Fact]
    public async Task Create_appends_and_numbers_cards_per_board()
    {
        var (client, boardId, todo) = await BoardAsync();
        var otherBoard = await client.CreateBoardAsync("Website relaunch");
        var otherTodo = (await client.GetListsAsync(otherBoard))[0].Id;

        var first = await client.CreateCardAsync(boardId, todo, "  Write spec  ");
        var second = await client.CreateCardAsync(boardId, todo, "Review spec");
        var elsewhere = await client.CreateCardAsync(otherBoard, otherTodo, "Pick fonts");

        Assert.Equal(("Write spec", 1, "ROA-1", 1024d), (first.Title, first.Number, first.Key, first.Position));
        Assert.Equal(("ROA-2", 2048d), (second.Key, second.Position));
        Assert.Equal("WR-1", elsewhere.Key);
        Assert.Equal(CardPriority.None, first.Priority);
        Assert.Equal(["Write spec", "Review spec"], (await client.CardTitlesAsync(boardId))["To do"]);
    }

    [Fact]
    public async Task Concurrent_creates_get_distinct_numbers()
    {
        var (client, boardId, todo) = await BoardAsync();

        var cards = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => client.CreateCardAsync(boardId, todo, $"Card {i}")));

        Assert.Equal([1, 2, 3, 4, 5], cards.Select(c => c.Number).Order());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_a_blank_title(string title)
    {
        var (client, boardId, todo) = await BoardAsync();

        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/lists/{todo}/cards", new CreateCard.Request(title));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_in_an_archived_list_is_a_conflict_and_on_another_board_not_found()
    {
        var (client, boardId, todo) = await BoardAsync();
        var (_, otherBoard, _) = await BoardAsync();
        (await client.PostEmptyAsync($"/api/boards/{boardId}/lists/{todo}/archive")).EnsureSuccessStatusCode();

        var archived = await client.PostAsJsonAsync($"/api/boards/{boardId}/lists/{todo}/cards", new CreateCard.Request("Late"));
        var foreign = await client.PostAsJsonAsync($"/api/boards/{otherBoard}/lists/{todo}/cards", new CreateCard.Request("Late"));

        Assert.Equal(HttpStatusCode.Conflict, archived.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Get_returns_the_detail()
    {
        var (client, boardId, todo) = await BoardAsync();
        var card = await client.CreateCardAsync(boardId, todo, "Write spec");
        (await client.PatchAsJsonAsync(Card(boardId, card.Id), new { description = "## Notes" })).EnsureSuccessStatusCode();

        var detail = await client.GetFromJsonAsync<GetCard.Response>(Card(boardId, card.Id));

        Assert.Equal(("Write spec", "## Notes", "Test User"), (detail!.Card.Title, detail.Description, detail.CreatedByName));
    }

    [Fact]
    public async Task Patch_changes_only_the_fields_sent_and_null_clears()
    {
        var (client, boardId, todo) = await BoardAsync();
        var card = await client.CreateCardAsync(boardId, todo, "Write spec");
        var due = new DateTimeOffset(2026, 11, 30, 17, 0, 0, TimeSpan.Zero);

        var first = await client.PatchAsJsonAsync(Card(boardId, card.Id), new
        {
            title = "Write the spec",
            dueAt = due,
            startAt = due.AddDays(-2),
            priority = "High",
            coverColor = "#0f766e",
        });
        first.EnsureSuccessStatusCode();
        var cleared = await client.PatchAsJsonAsync(Card(boardId, card.Id), new { dueAt = (DateTimeOffset?)null });
        var result = (await cleared.Content.ReadFromJsonAsync<CardSummary>())!;

        Assert.Equal(("Write the spec", CardPriority.High, "#0F766E"), (result.Title, result.Priority, result.CoverColor));
        Assert.Null(result.DueAt);
        Assert.Equal(due.AddDays(-2), result.StartAt);
    }

    [Fact]
    public async Task Patch_validates_title_colour_and_date_order()
    {
        var (client, boardId, todo) = await BoardAsync();
        var card = await client.CreateCardAsync(boardId, todo, "Write spec");
        var due = new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero);

        var invalid = await client.PatchAsJsonAsync(Card(boardId, card.Id), new { title = "", coverColor = "teal" });
        var order = await client.PatchAsJsonAsync(Card(boardId, card.Id), new { dueAt = due, startAt = due.AddDays(1) });
        var problem = await invalid.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("Title", problem!.Errors.Keys);
        Assert.Contains("CoverColor", problem.Errors.Keys);
        Assert.Equal(HttpStatusCode.BadRequest, order.StatusCode);
    }

    [Fact]
    public async Task Complete_and_reopen_are_idempotent()
    {
        var (client, boardId, todo) = await BoardAsync();
        var card = await client.CreateCardAsync(boardId, todo, "Write spec");

        await client.PostEmptyAsync($"{Card(boardId, card.Id)}/complete");
        var again = await client.PostEmptyAsync($"{Card(boardId, card.Id)}/complete");
        var done = (await again.Content.ReadFromJsonAsync<CardSummary>())!;
        var reopened = (await (await client.PostEmptyAsync($"{Card(boardId, card.Id)}/reopen")).Content.ReadFromJsonAsync<CardSummary>())!;

        Assert.NotNull(done.CompletedAt);
        Assert.Null(reopened.CompletedAt);
    }

    [Fact]
    public async Task Archive_returns_the_card_and_restore_puts_it_at_the_end()
    {
        var (client, boardId, todo) = await BoardAsync();
        var first = await client.CreateCardAsync(boardId, todo, "First");
        await client.CreateCardAsync(boardId, todo, "Second");

        var archived = (await (await client.PostEmptyAsync($"{Card(boardId, first.Id)}/archive")).Content.ReadFromJsonAsync<CardSummary>())!;
        var hidden = (await client.CardTitlesAsync(boardId))["To do"];
        (await client.PostEmptyAsync($"{Card(boardId, first.Id)}/restore")).EnsureSuccessStatusCode();

        Assert.NotNull(archived.ArchivedAt);
        Assert.Equal(["Second"], hidden);
        Assert.Equal(["Second", "First"], (await client.CardTitlesAsync(boardId))["To do"]);
    }

    [Fact]
    public async Task Restoring_a_card_whose_list_is_archived_is_a_conflict()
    {
        var (client, boardId, todo) = await BoardAsync();
        var card = await client.CreateCardAsync(boardId, todo, "First");
        await client.PostEmptyAsync($"{Card(boardId, card.Id)}/archive");
        await client.PostEmptyAsync($"/api/boards/{boardId}/lists/{todo}/archive");

        var restore = await client.PostEmptyAsync($"{Card(boardId, card.Id)}/restore");

        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
    }

    [Fact]
    public async Task Assignees_must_be_board_members()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var todo = (await board.Owner.GetListsAsync(board.BoardId))[0].Id;
        var card = await board.Owner.CreateCardAsync(board.BoardId, todo, "Write spec");
        var editorId = await board.Editor.GetUserIdAsync();
        var strangerId = await board.Stranger.GetUserIdAsync();
        var url = $"{Card(board.BoardId, card.Id)}/assignees";

        var nonMember = await board.Owner.PutAsJsonAsync(url, new SetCardAssignees.Request([editorId, strangerId]));
        var ok = await board.Owner.PutAsJsonAsync(url, new SetCardAssignees.Request([editorId, editorId]));
        var assigned = (await ok.Content.ReadFromJsonAsync<CardSummary>())!;

        Assert.Equal(HttpStatusCode.BadRequest, nonMember.StatusCode);
        Assert.Equal([editorId], assigned.Assignees.Select(a => a.UserId));
    }

    [Fact]
    public async Task Leaving_a_board_clears_the_members_assignments()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var todo = (await board.Owner.GetListsAsync(board.BoardId))[0].Id;
        var card = await board.Owner.CreateCardAsync(board.BoardId, todo, "Write spec");
        var editorId = await board.Editor.GetUserIdAsync();
        await board.Owner.PutAsJsonAsync($"{Card(board.BoardId, card.Id)}/assignees", new SetCardAssignees.Request([editorId]));

        (await board.Editor.DeleteAsync(new Uri($"/api/boards/{board.BoardId}/members/{editorId}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        var detail = await board.Owner.GetFromJsonAsync<GetCard.Response>(Card(board.BoardId, card.Id));
        Assert.Empty(detail!.Card.Assignees);
    }

    [Fact]
    public async Task Key_prefix_defaults_from_the_name_and_owners_can_change_it()
    {
        var (client, boardId, todo) = await BoardAsync("Website relaunch");
        var board = (await client.GetFromJsonAsync<GetBoard.Response>($"/api/boards/{boardId}"))!;
        Assert.Equal("WR", board.KeyPrefix);

        var invalid = await client.PutAsJsonAsync($"/api/boards/{boardId}", new UpdateBoard.Request(
            board.Name, board.Emoji, board.Description, board.Color, board.RowVersion, "web"));
        var updated = await client.PutAsJsonAsync($"/api/boards/{boardId}", new UpdateBoard.Request(
            board.Name, board.Emoji, board.Description, board.Color, board.RowVersion, "WEB"));
        updated.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("WEB-1", (await client.CreateCardAsync(boardId, todo, "Hero")).Key);
    }

    [Fact]
    public async Task Cards_on_an_archived_board_are_read_only()
    {
        var (client, boardId, todo) = await BoardAsync();
        var card = await client.CreateCardAsync(boardId, todo, "Write spec");
        (await client.PostEmptyAsync($"/api/boards/{boardId}/archive")).EnsureSuccessStatusCode();

        var create = await client.PostAsJsonAsync($"/api/boards/{boardId}/lists/{todo}/cards", new CreateCard.Request("Late"));
        var patch = await client.PatchAsJsonAsync(Card(boardId, card.Id), new { title = "Late" });

        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, patch.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri(Card(boardId, card.Id), UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_board_removes_its_cards_and_labels()
    {
        var (client, boardId, todo) = await BoardAsync();
        var card = await client.CreateCardAsync(boardId, todo, "Write spec");
        var label = await (await client.PostAsJsonAsync($"/api/boards/{boardId}/labels", new { name = "Bug", color = "#E11D48" }))
            .Content.ReadFromJsonAsync<LabelChip>();
        await client.PutAsJsonAsync($"{Card(boardId, card.Id)}/labels", new { labelIds = new[] { label!.Id } });
        await client.PutAsJsonAsync($"{Card(boardId, card.Id)}/assignees", new { userIds = new[] { await client.GetUserIdAsync() } });
        await client.PostEmptyAsync($"/api/boards/{boardId}/archive");

        var delete = await client.DeleteAsync(new Uri($"/api/boards/{boardId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        await factory.WithDbAsync(db =>
        {
            Assert.False(db.Cards.Any(c => c.BoardId == boardId));
            Assert.False(db.Labels.Any(l => l.BoardId == boardId));
            return Task.CompletedTask;
        });
    }

    public static TheoryData<string, string, string> Writes => new()
    {
        { "POST", "complete", "" },
        { "POST", "reopen", "" },
        { "POST", "archive", "" },
        { "POST", "restore", "" },
        { "PATCH", "", """{"title":"New"}""" },
        { "PUT", "labels", """{"labelIds":[]}""" },
        { "PUT", "assignees", """{"userIds":[]}""" },
    };

    [Theory]
    [MemberData(nameof(Writes))]
    public async Task Card_writes_need_an_editor(string method, string path, string body)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var todo = (await board.Owner.GetListsAsync(board.BoardId))[0].Id;
        var card = await board.Owner.CreateCardAsync(board.BoardId, todo, "Write spec");
        var url = path.Length == 0 ? Card(board.BoardId, card.Id) : $"{Card(board.BoardId, card.Id)}/{path}";

        HttpRequestMessage Request() => new(new HttpMethod(method), new Uri(url, UriKind.Relative))
        {
            Content = body.Length == 0 ? null : new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await board.Viewer.SendAsync(Request())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.Stranger.SendAsync(Request())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await board.Editor.SendAsync(Request())).StatusCode);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.Created)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_editors_create(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var todo = (await board.Owner.GetListsAsync(board.BoardId))[0].Id;

        var response = await board.For(role).PostAsJsonAsync(
            $"/api/boards/{board.BoardId}/lists/{todo}/cards", new CreateCard.Request("Idea"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("Viewer", HttpStatusCode.OK)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Members_read_cards_and_strangers_get_404(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var todo = (await board.Owner.GetListsAsync(board.BoardId))[0].Id;
        var card = await board.Owner.CreateCardAsync(board.BoardId, todo, "Write spec");

        var response = await board.For(role).GetAsync(new Uri(Card(board.BoardId, card.Id), UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }
}
