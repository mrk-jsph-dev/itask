using System.Net;
using System.Net.Http.Json;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Activity;

public sealed class ActivityFeedTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string BoardFeed(Guid boardId) => $"/api/boards/{boardId}/activity";

    [Fact]
    public async Task Board_feed_is_newest_first_with_names_and_card_snapshot()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var lists = await card.Board.Owner.GetListsAsync(card.BoardId);
        (await card.Board.Editor.MoveCardAsync(card.BoardId, await card.SummaryAsync(), lists[1].Id)).EnsureSuccessStatusCode();

        var page = await card.Board.Viewer.GetActivityAsync(BoardFeed(card.BoardId));

        var latest = page.Items[0];
        Assert.Equal("card.moved", latest.Type);
        Assert.Equal(card.CardId, latest.CardId);
        Assert.Equal("Write spec", latest.Data.GetProperty("card").GetProperty("title").GetString());
        Assert.Equal(lists[0].Name, latest.Data.GetProperty("fromList").GetString());
        Assert.Equal(lists[1].Name, latest.Data.GetProperty("toList").GetString());
        Assert.Equal("Team board", latest.Board.Name);
        Assert.Equal("Test User", latest.Actor.DisplayName);
        Assert.Equal("board.created", page.Items[^1].Type);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task Pages_follow_the_cursor_without_overlap()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        for (var i = 0; i < 4; i++)
        {
            await card.AddItemAsync(card.Board.Owner, $"Step {i}");
        }

        var all = await card.Board.Owner.GetActivityAsync(BoardFeed(card.BoardId));
        var first = await card.Board.Owner.GetActivityAsync($"{BoardFeed(card.BoardId)}?limit=3");
        var second = await card.Board.Owner.GetActivityAsync($"{BoardFeed(card.BoardId)}?limit=3&cursor={first.NextCursor}");

        Assert.NotNull(first.NextCursor);
        Assert.Equal(all.Items.Select(a => a.Id).Take(6), first.Items.Concat(second.Items).Select(a => a.Id));
    }

    [Theory]
    [InlineData("?cursor=nope")]
    [InlineData("?type=cards")]
    public async Task Bad_cursor_or_type_is_400(string query)
    {
        var card = await CardOnBoard.CreateAsync(factory);

        var response = await card.Board.Owner.GetAsync(new Uri(BoardFeed(card.BoardId) + query, UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Filters_by_category_and_member()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        (await card.Board.Viewer.PostAsJsonAsync($"{card.Url}/comments", new { body = "Hi" })).EnsureSuccessStatusCode();
        await card.AddItemAsync(card.Board.Editor, "Step");
        var viewerId = await card.Board.Viewer.GetUserIdAsync();

        var comments = await card.Board.Owner.GetActivityAsync($"{BoardFeed(card.BoardId)}?type=comment");
        var byViewer = await card.Board.Owner.GetActivityAsync($"{BoardFeed(card.BoardId)}?actorId={viewerId}");

        Assert.Equal(["comment.added"], comments.Items.Select(a => a.Type));
        Assert.Equal(["comment.added", "member.joined"], byViewer.Items.Select(a => a.Type));
    }

    [Fact]
    public async Task Card_feed_holds_only_that_card()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var todo = (await card.Board.Owner.GetListsAsync(card.BoardId))[0].Id;
        await card.Board.Owner.CreateCardAsync(card.BoardId, todo, "Other card");
        await card.AddItemAsync(card.Board.Owner, "Step");

        var page = await card.Board.Viewer.GetActivityAsync($"{card.Url}/activity");

        Assert.Equal(["checklist.added", "card.created"], page.Items.Select(a => a.Type));
        Assert.All(page.Items, a => Assert.Equal(card.CardId, a.CardId));
    }

    [Fact]
    public async Task Non_members_get_404()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var stranger = card.Board.Stranger;

        var board = await stranger.GetAsync(new Uri(BoardFeed(card.BoardId), UriKind.Relative));
        var cardFeed = await stranger.GetAsync(new Uri($"{card.Url}/activity", UriKind.Relative));
        var unknownCard = await card.Board.Owner.GetAsync(
            new Uri($"{CardTestKit.Cards(card.BoardId)}/{Guid.NewGuid()}/activity", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, board.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cardFeed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownCard.StatusCode);
    }

    [Fact]
    public async Task My_feed_shows_my_changes_on_boards_i_can_still_see()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var editor = card.Board.Editor;
        await card.AddItemAsync(editor, "Mine");
        await card.AddItemAsync(card.Board.Owner, "Owner's");
        var otherBoard = await editor.CreateBoardAsync("Side project");

        var mine = await editor.GetActivityAsync("/api/me/activity");
        Assert.Equal(["board.created", "checklist.added", "member.joined"], mine.Items.Select(a => a.Type));
        Assert.Equal(otherBoard, mine.Items[0].Board.Id);

        var editorId = await editor.GetUserIdAsync();
        (await editor.DeleteAsync(new Uri($"/api/boards/{card.BoardId}/members/{editorId}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        var afterLeaving = await editor.GetActivityAsync("/api/me/activity");
        Assert.Equal(["board.created"], afterLeaving.Items.Select(a => a.Type));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await editor.GetAsync(new Uri(BoardFeed(card.BoardId), UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_board_removes_its_activity()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        await card.ArchiveBoardAsync();

        (await card.Board.Owner.DeleteAsync(new Uri($"/api/boards/{card.BoardId}", UriKind.Relative))).EnsureSuccessStatusCode();

        Assert.Empty(await factory.ActivityTypesAsync(card.BoardId));
    }

    [Fact]
    public async Task Feeds_need_sign_in()
    {
        var response = await factory.CreateHttpsClient().GetAsync(new Uri("/api/me/activity", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
