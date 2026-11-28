using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Realtime;
using ITask.Api.Domain;
using ITask.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.SignalR;

namespace ITask.Api.Tests.Features.Realtime;

public sealed class LiveUpdatesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Board(Guid Id, LiveUser Owner, LiveUser Editor, LiveUser Viewer, Guid TodoId, Guid DoingId);

    private async Task<Board> CreateBoardAsync()
    {
        var owner = await factory.CreateLiveUserAsync();
        var boardId = await owner.Client.CreateBoardAsync();
        var editor = await factory.JoinLiveAsync(owner.Client, boardId, BoardRole.Editor);
        var viewer = await factory.JoinLiveAsync(owner.Client, boardId, BoardRole.Viewer);
        var lists = await owner.Client.GetListsAsync(boardId);
        return new Board(boardId, owner, editor, viewer, lists[0].Id, lists[1].Id);
    }

    private async Task<LiveConnection> JoinAsync(LiveUser user, Guid boardId)
    {
        var connection = await factory.ConnectAsync(user);
        await connection.JoinAsync(boardId);
        return connection;
    }

    [Fact]
    public async Task Members_see_card_moves_with_the_new_list_and_position()
    {
        var board = await CreateBoardAsync();
        var card = await board.Editor.Client.CreateCardAsync(board.Id, board.TodoId, "Ship it");
        await using var viewer = await JoinAsync(board.Viewer, board.Id);

        (await board.Editor.Client.MoveCardAsync(board.Id, card, board.DoingId)).EnsureSuccessStatusCode();

        var moved = await LiveConnection.NextAsync(viewer.Events);
        Assert.Equal(
            (ActivityTypes.CardMoved, board.Id, (Guid?)card.Id, (Guid?)board.DoingId),
            (moved.Type, moved.BoardId, moved.CardId, moved.ListId));
        Assert.NotNull(moved.Position);
        Assert.Equal(await board.Editor.Client.GetUserIdAsync(), moved.ActorId);
    }

    [Fact]
    public async Task Completing_a_blocker_pushes_the_unblocked_card()
    {
        var board = await CreateBoardAsync();
        var waiting = await board.Editor.Client.CreateCardAsync(board.Id, board.TodoId, "Ship it");
        var blocker = await board.Editor.Client.CreateCardAsync(board.Id, board.TodoId, "Test it");
        (await board.Editor.Client.PutAsync(
            new Uri($"{CardTestKit.Cards(board.Id)}/{waiting.Id}/blocked-by/{blocker.Id}", UriKind.Relative), null))
            .EnsureSuccessStatusCode();
        await using var viewer = await JoinAsync(board.Viewer, board.Id);

        (await board.Editor.Client.PostEmptyAsync($"{CardTestKit.Cards(board.Id)}/{blocker.Id}/complete")).EnsureSuccessStatusCode();

        var events = new[] { await LiveConnection.NextAsync(viewer.Events), await LiveConnection.NextAsync(viewer.Events) };
        Assert.Contains(events, e => e.Type == ActivityTypes.CardCompleted && e.CardId == blocker.Id);
        Assert.Contains(events, e => e.Type == ActivityTypes.CardUnblocked && e.CardId == waiting.Id);
    }

    [Fact]
    public async Task Non_members_cannot_join()
    {
        var board = await CreateBoardAsync();
        var stranger = await factory.CreateLiveUserAsync();
        await using var connection = await factory.ConnectAsync(stranger);

        var error = await Assert.ThrowsAsync<HubException>(() => connection.JoinAsync(board.Id));
        Assert.Contains("not found", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Anonymous_clients_cannot_connect()
    {
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => factory.ConnectAsync(null));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task The_acting_tab_is_skipped_but_the_users_other_tabs_get_the_event()
    {
        var board = await CreateBoardAsync();
        await using var acting = await JoinAsync(board.Owner, board.Id);
        await using var otherTab = await JoinAsync(board.Owner, board.Id);

        board.Owner.Client.ActAs(acting);
        await board.Owner.Client.CreateCardAsync(board.Id, board.TodoId, "From the acting tab");
        board.Owner.Client.ActAs(null);
        await board.Owner.Client.CreateCardAsync(board.Id, board.TodoId, "From nowhere");

        Assert.Equal(ActivityTypes.CardCreated, (await LiveConnection.NextAsync(otherTab.Events)).Type);
        Assert.Equal(ActivityTypes.CardCreated, (await LiveConnection.NextAsync(otherTab.Events)).Type);
        Assert.Equal(ActivityTypes.CardCreated, (await LiveConnection.NextAsync(acting.Events)).Type);
        await Task.Delay(200);
        Assert.False(acting.Events.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Comment_events_carry_ids_only()
    {
        var board = await CreateBoardAsync();
        var card = await board.Owner.Client.CreateCardAsync(board.Id, board.TodoId, "Secret plan");
        await using var editor = await JoinAsync(board.Editor, board.Id);

        (await board.Viewer.Client.PostAsJsonAsync($"/api/boards/{board.Id}/cards/{card.Id}/comments", new { body = "the password is hunter2" }))
            .EnsureSuccessStatusCode();

        var added = await LiveConnection.NextAsync(editor.Events);
        Assert.Equal((ActivityTypes.CommentAdded, (Guid?)card.Id), (added.Type, added.CardId));
        var json = JsonSerializer.Serialize(added);
        Assert.DoesNotContain("hunter2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret plan", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Presence_lists_who_has_the_board_open()
    {
        var board = await CreateBoardAsync();
        await using var owner = await factory.ConnectAsync(board.Owner);
        Assert.Single(await owner.JoinAsync(board.Id));

        var editor = await factory.ConnectAsync(board.Editor);
        var present = await editor.JoinAsync(board.Id);
        Assert.Equal(2, present.Count);
        Assert.Equal(2, (await owner.PresenceOfAsync(2)).Users.Count);

        await editor.DisposeAsync();
        var after = await owner.PresenceOfAsync(1);
        Assert.Equal(await board.Owner.Client.GetUserIdAsync(), after.Users[0].UserId);
    }

    [Fact]
    public async Task A_removed_member_loses_the_live_feed()
    {
        var board = await CreateBoardAsync();
        await using var viewer = await JoinAsync(board.Viewer, board.Id);
        await using var editor = await JoinAsync(board.Editor, board.Id);
        var viewerId = await board.Viewer.Client.GetUserIdAsync();

        (await board.Owner.Client.DeleteAsync(new Uri($"/api/boards/{board.Id}/members/{viewerId}", UriKind.Relative)))
            .EnsureSuccessStatusCode();

        Assert.Equal(board.Id, (await LiveConnection.NextAsync(viewer.AccessLost)).BoardId);
        var removed = await LiveConnection.NextAsync(editor.Events);
        Assert.Equal((ActivityTypes.MemberRemoved, (Guid?)viewerId), (removed.Type, removed.UserId));

        await board.Owner.Client.CreateCardAsync(board.Id, board.TodoId, "After you left");
        Assert.Equal(ActivityTypes.CardCreated, (await LiveConnection.NextAsync(editor.Events)).Type);
        await Task.Delay(200);
        Assert.False(viewer.Events.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Deleting_the_board_tells_everyone_on_it()
    {
        var board = await CreateBoardAsync();
        await using var editor = await JoinAsync(board.Editor, board.Id);
        (await board.Owner.Client.PostEmptyAsync($"/api/boards/{board.Id}/archive")).EnsureSuccessStatusCode();
        Assert.Equal(ActivityTypes.BoardArchived, (await LiveConnection.NextAsync(editor.Events)).Type);

        (await board.Owner.Client.DeleteAsync(new Uri($"/api/boards/{board.Id}", UriKind.Relative))).EnsureSuccessStatusCode();

        Assert.Equal(LiveTypes.BoardDeleted, (await LiveConnection.NextAsync(editor.Events)).Type);
    }
}
