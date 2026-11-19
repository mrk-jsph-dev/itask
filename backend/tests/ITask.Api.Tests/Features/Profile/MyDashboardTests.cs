using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Cards.SetCardAssignees;
using ITask.Api.Features.Profile.GetDueSoon;
using ITask.Api.Features.Profile.GetMyStats;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Profile;

public sealed class MyDashboardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Card(Guid boardId, Guid cardId) => $"{CardTestKit.Cards(boardId)}/{cardId}";

    [Fact]
    public async Task Stats_count_my_boards_assigned_cards_and_completed_ones()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var owner = board.Owner;
        var ownerId = await owner.GetUserIdAsync();
        var todo = (await owner.GetListsAsync(board.BoardId))[0].Id;
        var mine = await owner.CreateCardAsync(board.BoardId, todo, "Mine");
        var done = await owner.CreateCardAsync(board.BoardId, todo, "Mine and done");
        await owner.CreateCardAsync(board.BoardId, todo, "Nobody's");
        foreach (var id in new[] { mine.Id, done.Id })
        {
            (await owner.PutAsJsonAsync($"{Card(board.BoardId, id)}/assignees", new SetCardAssignees.Request([ownerId])))
                .EnsureSuccessStatusCode();
        }

        (await owner.PostEmptyAsync($"{Card(board.BoardId, done.Id)}/complete")).EnsureSuccessStatusCode();
        await owner.CreateBoardAsync("Second");
        var archived = await owner.CreateBoardAsync("Old");
        (await owner.PostEmptyAsync($"/api/boards/{archived}/archive")).EnsureSuccessStatusCode();

        var stats = await owner.GetFromJsonAsync<GetMyStats.Response>("/api/me/stats");
        var stranger = await board.Stranger.GetFromJsonAsync<GetMyStats.Response>("/api/me/stats");

        Assert.Equal(new GetMyStats.Response(2, 2, 1), stats);
        Assert.Equal(new GetMyStats.Response(0, 0, 0), stranger);
    }

    [Fact]
    public async Task Due_soon_lists_mine_and_unassigned_cards_due_within_a_week()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var owner = board.Owner;
        var editorId = await board.Editor.GetUserIdAsync();
        var todo = (await owner.GetListsAsync(board.BoardId))[0].Id;
        var now = DateTimeOffset.UtcNow;

        async Task<Guid> CardAsync(string title, DateTimeOffset? due)
        {
            var card = await owner.CreateCardAsync(board.BoardId, todo, title);
            (await owner.PatchAsJsonAsync(Card(board.BoardId, card.Id), new { dueAt = due, emoji = "⏰" })).EnsureSuccessStatusCode();
            return card.Id;
        }

        await CardAsync("Overdue", now.AddDays(-1));
        await CardAsync("Tomorrow", now.AddDays(1));
        await CardAsync("Next month", now.AddDays(30));
        await CardAsync("No date", null);
        var editors = await CardAsync("Editor's", now.AddDays(2));
        (await owner.PutAsJsonAsync($"{Card(board.BoardId, editors)}/assignees", new SetCardAssignees.Request([editorId])))
            .EnsureSuccessStatusCode();
        var finished = await CardAsync("Finished", now.AddDays(1));
        (await owner.PostEmptyAsync($"{Card(board.BoardId, finished)}/complete")).EnsureSuccessStatusCode();

        var mine = (await owner.GetFromJsonAsync<GetDueSoon.Response>("/api/me/due-soon"))!;
        var editor = (await board.Editor.GetFromJsonAsync<GetDueSoon.Response>("/api/me/due-soon"))!;
        var stranger = (await board.Stranger.GetFromJsonAsync<GetDueSoon.Response>("/api/me/due-soon"))!;

        Assert.Equal(["Overdue", "Tomorrow"], mine.Items.Select(i => i.Title));
        Assert.Equal((2, 1), (mine.DueThisWeek, mine.Overdue));
        Assert.Equal(("Team board", "📋", "⏰"), (mine.Items[0].BoardName, mine.Items[0].BoardEmoji, mine.Items[0].Emoji));
        Assert.Equal(["Overdue", "Tomorrow", "Editor's"], editor.Items.Select(i => i.Title));
        Assert.Empty(stranger.Items);
        Assert.Equal(0, stranger.DueThisWeek);
    }

    [Fact]
    public async Task Due_soon_skips_archived_boards()
    {
        var card = await CardOnBoard.CreateAsync(factory);
        var owner = card.Board.Owner;
        (await owner.PatchAsJsonAsync(card.Url, new { dueAt = DateTimeOffset.UtcNow.AddDays(1) })).EnsureSuccessStatusCode();
        Assert.Single((await owner.GetFromJsonAsync<GetDueSoon.Response>("/api/me/due-soon"))!.Items);

        await card.ArchiveBoardAsync();

        Assert.Empty((await owner.GetFromJsonAsync<GetDueSoon.Response>("/api/me/due-soon"))!.Items);
    }

    [Theory]
    [InlineData("/api/me/stats")]
    [InlineData("/api/me/due-soon")]
    public async Task Dashboard_endpoints_need_sign_in(string url)
    {
        var response = await factory.CreateHttpsClient().GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
