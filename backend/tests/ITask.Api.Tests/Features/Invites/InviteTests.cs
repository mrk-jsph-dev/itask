using System.Net;
using System.Net.Http.Json;
using ITask.Api.Domain;
using ITask.Api.Features.Invites.AcceptInvite;
using ITask.Api.Features.Invites.CreateInvite;
using ITask.Api.Features.Invites.ListInvites;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Tests.Features.Invites;

public sealed class InviteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Link_invite_adds_the_member_with_the_invited_role()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        var invite = await owner.InviteAsync(boardId, BoardRole.Editor);
        var joiner = await factory.CreateSignedInClientAsync();

        var response = await joiner.PostAsync(Url($"/api/invites/{invite.Token}/accept"), null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(boardId, (await response.Content.ReadFromJsonAsync<AcceptInvite.Response>())!.BoardId);
        var board = await joiner.GetFromJsonAsync<ITask.Api.Features.Boards.GetBoard.GetBoard.Response>($"/api/boards/{boardId}");
        Assert.Equal(BoardRole.Editor, board!.Role);
    }

    [Fact]
    public async Task Invites_are_single_use()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        var invite = await owner.InviteAsync(boardId, BoardRole.Viewer);
        (await (await factory.CreateSignedInClientAsync()).PostAsync(Url($"/api/invites/{invite.Token}/accept"), null))
            .EnsureSuccessStatusCode();

        var second = await (await factory.CreateSignedInClientAsync()).PostAsync(Url($"/api/invites/{invite.Token}/accept"), null);

        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);
    }

    [Fact]
    public async Task Expired_invites_are_gone()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        var invite = await owner.InviteAsync(boardId, BoardRole.Viewer);
        await factory.WithDbAsync(async db =>
        {
            var row = await db.BoardInvites.SingleAsync(i => i.Id == invite.Id);
            row.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });

        var response = await (await factory.CreateSignedInClientAsync()).PostAsync(Url($"/api/invites/{invite.Token}/accept"), null);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_tokens_are_not_found()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsync(Url("/api/invites/not-a-real-token/accept"), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Email_invites_only_work_for_that_address()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        var email = $"invitee-{Guid.NewGuid():N}@example.com";
        var invite = await owner.InviteAsync(boardId, BoardRole.Viewer, email);

        var wrong = await (await factory.CreateSignedInClientAsync()).PostAsync(Url($"/api/invites/{invite.Token}/accept"), null);
        var right = await (await factory.CreateSignedInClientAsync(email.ToUpperInvariant()))
            .PostAsync(Url($"/api/invites/{invite.Token}/accept"), null);

        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
    }

    [Fact]
    public async Task Revoked_invites_stop_working_and_leave_the_pending_list()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();
        var invite = await owner.InviteAsync(boardId, BoardRole.Viewer);
        Assert.Single((await owner.GetFromJsonAsync<List<ListInvites.Item>>($"/api/boards/{boardId}/invites"))!);

        var revoke = await owner.DeleteAsync(Url($"/api/boards/{boardId}/invites/{invite.Id}"));
        var accept = await (await factory.CreateSignedInClientAsync()).PostAsync(Url($"/api/invites/{invite.Token}/accept"), null);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<List<ListInvites.Item>>($"/api/boards/{boardId}/invites"))!);
    }

    [Fact]
    public async Task Existing_members_keep_their_role()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var invite = await board.Owner.InviteAsync(board.BoardId, BoardRole.Owner);

        (await board.Viewer.PostAsync(Url($"/api/invites/{invite.Token}/accept"), null)).EnsureSuccessStatusCode();

        var view = await board.Viewer.GetFromJsonAsync<ITask.Api.Features.Boards.GetBoard.GetBoard.Response>($"/api/boards/{board.BoardId}");
        Assert.Equal(BoardRole.Viewer, view!.Role);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.Forbidden)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_owners_manage_invites(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var invite = await board.Owner.InviteAsync(board.BoardId, BoardRole.Viewer);
        var client = board.For(role);

        var create = await client.PostAsJsonAsync(
            $"/api/boards/{board.BoardId}/invites", new CreateInvite.Request(null, BoardRole.Owner));
        var list = await client.GetAsync(Url($"/api/boards/{board.BoardId}/invites"));
        var revoke = await client.DeleteAsync(Url($"/api/boards/{board.BoardId}/invites/{invite.Id}"));

        Assert.Equal(expected, create.StatusCode);
        Assert.Equal(expected, list.StatusCode);
        Assert.Equal(expected, revoke.StatusCode);
    }

    [Fact]
    public async Task Invalid_email_is_rejected()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var boardId = await owner.CreateBoardAsync();

        var response = await owner.PostAsJsonAsync(
            $"/api/boards/{boardId}/invites", new CreateInvite.Request("not-an-email", BoardRole.Viewer));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
