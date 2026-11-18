using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Features.Cards.GetCard;
using ITask.Api.Features.Checklist.MoveChecklistItem;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Checklist;

public sealed class ChecklistTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<List<string>> TextsAsync(CardOnBoard card) =>
        [.. (await card.Board.Owner.GetFromJsonAsync<GetCard.Response>(card.Url))!.Checklist.Select(i => i.Text)];

    [Fact]
    public async Task Add_appends_and_the_tile_counts_items()
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var first = await card.AddItemAsync(card.Board.Editor, "  Draft  ");
        var second = await card.AddItemAsync(card.Board.Editor, "Review");
        (await card.Board.Editor.PutAsJsonAsync($"{card.Url}/checklist/{first.Id}/done", new { isDone = true }))
            .EnsureSuccessStatusCode();

        var summary = await card.SummaryAsync();
        Assert.Equal(("Draft", 1024d, 2048d), (first.Text, first.Position, second.Position));
        Assert.Equal((1, 2), (summary.ChecklistDone, summary.ChecklistTotal));
        Assert.Equal(["Draft", "Review"], await TextsAsync(card));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Add_rejects_blank_text(string text)
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var response = await card.Board.Owner.PostAsJsonAsync($"{card.Url}/checklist", new { text });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Add_rejects_long_text_and_a_non_member_assignee()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var strangerId = await card.Board.Stranger.GetUserIdAsync();

        var tooLong = await card.Board.Owner.PostAsJsonAsync($"{card.Url}/checklist", new { text = new string('x', 301) });
        var stranger = await card.Board.Owner.PostAsJsonAsync(
            $"{card.Url}/checklist", new { text = "Hand off", assigneeId = strangerId });

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, stranger.StatusCode);
    }

    [Fact]
    public async Task Patch_changes_fields_and_null_clears()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var item = await card.AddItemAsync(card.Board.Owner, "Draft");
        var editorId = await card.Board.Editor.GetUserIdAsync();
        var due = new DateTimeOffset(2026, 12, 1, 17, 0, 0, TimeSpan.Zero);

        (await card.Board.Owner.PatchAsJsonAsync(
            $"{card.Url}/checklist/{item.Id}", new { text = "Draft v2", dueAt = due, assigneeId = editorId }))
            .EnsureSuccessStatusCode();
        var cleared = await card.Board.Owner.PatchAsJsonAsync($"{card.Url}/checklist/{item.Id}", new { dueAt = (DateTimeOffset?)null });
        var result = (await cleared.Content.ReadFromJsonAsync<ChecklistItemView>())!;

        Assert.Equal(("Draft v2", (DateTimeOffset?)null, (Guid?)editorId), (result.Text, result.DueAt, result.AssigneeId));
    }

    [Fact]
    public async Task Toggle_is_idempotent()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var item = await card.AddItemAsync(card.Board.Owner, "Draft");

        await card.Board.Owner.PutAsJsonAsync($"{card.Url}/checklist/{item.Id}/done", new { isDone = true });
        var again = await card.Board.Owner.PutAsJsonAsync($"{card.Url}/checklist/{item.Id}/done", new { isDone = true });

        Assert.True((await again.Content.ReadFromJsonAsync<ChecklistItemView>())!.IsDone);
    }

    [Fact]
    public async Task Move_reorders_and_rejects_stale_neighbours()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var a = await card.AddItemAsync(card.Board.Owner, "A");
        var b = await card.AddItemAsync(card.Board.Owner, "B");
        var c = await card.AddItemAsync(card.Board.Owner, "C");

        var moved = await card.Board.Owner.PutAsJsonAsync(
            $"{card.Url}/checklist/{c.Id}/position", new MoveChecklistItem.Request(null, a.Id));
        var stale = await card.Board.Owner.PutAsJsonAsync(
            $"{card.Url}/checklist/{a.Id}/position", new MoveChecklistItem.Request(b.Id, c.Id));

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(["C", "A", "B"], await TextsAsync(card));
    }

    [Fact]
    public async Task Delete_removes_the_item()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var item = await card.AddItemAsync(card.Board.Owner, "Draft");

        var deleted = await card.Board.Editor.DeleteAsync(new Uri($"{card.Url}/checklist/{item.Id}", UriKind.Relative));
        var again = await card.Board.Editor.DeleteAsync(new Uri($"{card.Url}/checklist/{item.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Empty(await TextsAsync(card));
    }

    [Fact]
    public async Task Viewer_is_forbidden_and_stranger_not_found()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var item = await card.AddItemAsync(card.Board.Owner, "Draft");

        var viewerAdd = await card.Board.Viewer.PostAsJsonAsync($"{card.Url}/checklist", new { text = "Mine" });
        var viewerToggle = await card.Board.Viewer.PutAsJsonAsync($"{card.Url}/checklist/{item.Id}/done", new { isDone = true });
        var strangerAdd = await card.Board.Stranger.PostAsJsonAsync($"{card.Url}/checklist", new { text = "Mine" });
        var viewerRead = await card.Board.Viewer.GetFromJsonAsync<GetCard.Response>(card.Url);

        Assert.Equal(HttpStatusCode.Forbidden, viewerAdd.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewerToggle.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerAdd.StatusCode);
        Assert.Single(viewerRead!.Checklist);
    }

    [Fact]
    public async Task Archived_board_is_a_conflict_and_a_foreign_card_not_found()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var other = await CardOnBoard.CreateAsync(factory);
        var item = await other.AddItemAsync(other.Board.Owner, "Theirs");

        // The owner of one board can't reach another board's item through their own board's URL.
        var foreign = await card.Board.Owner.PutAsJsonAsync($"{card.Url}/checklist/{item.Id}/done", new { isDone = true });
        await card.ArchiveBoardAsync();
        var archived = await card.Board.Owner.PostAsJsonAsync($"{card.Url}/checklist", new { text = "Late" });

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, archived.StatusCode);
    }

    [Fact]
    public async Task Leaving_the_board_clears_checklist_assignments()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var editorId = await card.Board.Editor.GetUserIdAsync();
        var response = await card.Board.Owner.PostAsJsonAsync(
            $"{card.Url}/checklist", new { text = "Hand off", assigneeId = editorId });
        response.EnsureSuccessStatusCode();

        (await card.Board.Editor.DeleteAsync(new Uri($"/api/boards/{card.BoardId}/members/{editorId}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        var detail = await card.Board.Owner.GetFromJsonAsync<GetCard.Response>(card.Url);
        Assert.Null(detail!.Checklist.Single().AssigneeId);
    }
}
