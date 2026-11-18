using System.Net;
using System.Net.Http.Json;
using ITask.Api.Domain;
using ITask.Api.Features.Boards.ChangeMemberRole;
using ITask.Api.Features.Boards.ListMembers;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Boards;

public sealed class MemberTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Members_see_the_member_list_owners_first()
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var members = await board.Viewer.GetFromJsonAsync<List<ListMembers.Item>>($"/api/boards/{board.BoardId}/members");

        Assert.Equal([BoardRole.Owner, BoardRole.Editor, BoardRole.Viewer], members!.Select(m => m.Role));
    }

    [Fact]
    public async Task Strangers_cannot_list_members()
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var response = await board.Stranger.GetAsync(Url($"/api/boards/{board.BoardId}/members"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Owner_changes_a_role()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var viewerId = await board.Viewer.GetUserIdAsync();

        var response = await board.Owner.PutAsJsonAsync(
            $"/api/boards/{board.BoardId}/members/{viewerId}", new ChangeMemberRole.Request(BoardRole.Editor));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var members = await board.Owner.GetFromJsonAsync<List<ListMembers.Item>>($"/api/boards/{board.BoardId}/members");
        Assert.Equal(BoardRole.Editor, members!.Single(m => m.UserId == viewerId).Role);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.Forbidden)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_owners_change_roles(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var viewerId = await board.Viewer.GetUserIdAsync();

        var response = await board.For(role).PutAsJsonAsync(
            $"/api/boards/{board.BoardId}/members/{viewerId}", new ChangeMemberRole.Request(BoardRole.Owner));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Last_owner_cannot_be_demoted_or_leave()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var ownerId = await board.Owner.GetUserIdAsync();

        var demote = await board.Owner.PutAsJsonAsync(
            $"/api/boards/{board.BoardId}/members/{ownerId}", new ChangeMemberRole.Request(BoardRole.Editor));
        var leave = await board.Owner.DeleteAsync(Url($"/api/boards/{board.BoardId}/members/{ownerId}"));

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
    }

    [Fact]
    public async Task Owner_can_leave_once_another_owner_exists()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var ownerId = await board.Owner.GetUserIdAsync();
        var editorId = await board.Editor.GetUserIdAsync();
        await board.Owner.PutAsJsonAsync(
            $"/api/boards/{board.BoardId}/members/{editorId}", new ChangeMemberRole.Request(BoardRole.Owner));

        var leave = await board.Owner.DeleteAsync(Url($"/api/boards/{board.BoardId}/members/{ownerId}"));

        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.Owner.GetAsync(Url($"/api/boards/{board.BoardId}"))).StatusCode);
    }

    [Fact]
    public async Task Members_can_leave_but_not_remove_others()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var viewerId = await board.Viewer.GetUserIdAsync();
        var editorId = await board.Editor.GetUserIdAsync();

        var removeOther = await board.Viewer.DeleteAsync(Url($"/api/boards/{board.BoardId}/members/{editorId}"));
        var leave = await board.Viewer.DeleteAsync(Url($"/api/boards/{board.BoardId}/members/{viewerId}"));

        Assert.Equal(HttpStatusCode.Forbidden, removeOther.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, leave.StatusCode);
    }

    [Fact]
    public async Task Owner_removes_a_member()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var editorId = await board.Editor.GetUserIdAsync();

        var response = await board.Owner.DeleteAsync(Url($"/api/boards/{board.BoardId}/members/{editorId}"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.Editor.GetAsync(Url($"/api/boards/{board.BoardId}"))).StatusCode);
    }
}
