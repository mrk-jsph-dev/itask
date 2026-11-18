using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Boards.CreateBoard;
using ITask.Api.Features.Boards.GetBoard;
using ITask.Api.Features.Boards.GetMyBoards;
using ITask.Api.Features.Boards.UpdateBoard;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Boards;

public sealed class BoardSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Create_stores_description_and_colour()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync(
            "/api/boards", new CreateBoard.Request("Roadmap", "🗺", "Q1 plans", "#0f766e"));
        var created = await response.Content.ReadFromJsonAsync<CreateBoard.Response>();

        var board = await client.GetFromJsonAsync<GetBoard.Response>($"/api/boards/{created!.Id}");
        Assert.Equal("Q1 plans", board!.Description);
        Assert.Equal("#0F766E", board.Color);
        Assert.Equal(1, board.MemberCount);
        Assert.False(board.IsStarred);
    }

    [Fact]
    public async Task Create_rejects_a_bad_colour()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/boards", new CreateBoard.Request("Roadmap", null, null, "blue"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Owner_updates_details_and_gets_a_new_row_version()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var before = await client.GetFromJsonAsync<GetBoard.Response>($"/api/boards/{boardId}");

        var response = await client.PutAsJsonAsync(
            $"/api/boards/{boardId}",
            new UpdateBoard.Request("Renamed", "✅", "New text", "#B91C1C", before!.RowVersion));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await client.GetFromJsonAsync<GetBoard.Response>($"/api/boards/{boardId}");
        Assert.Equal("Renamed", after!.Name);
        Assert.Equal("New text", after.Description);
        Assert.NotEqual(before.RowVersion, after.RowVersion);
    }

    [Fact]
    public async Task Stale_row_version_is_a_conflict()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var board = await client.GetFromJsonAsync<GetBoard.Response>($"/api/boards/{boardId}");
        var first = new UpdateBoard.Request("First", null, null, "#1D5BF5", board!.RowVersion);
        (await client.PutAsJsonAsync($"/api/boards/{boardId}", first)).EnsureSuccessStatusCode();

        var response = await client.PutAsJsonAsync($"/api/boards/{boardId}", first with { Name = "Second" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.Forbidden)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_owners_update(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var current = await board.Owner.GetFromJsonAsync<GetBoard.Response>($"/api/boards/{board.BoardId}");

        var response = await board.For(role).PutAsJsonAsync(
            $"/api/boards/{board.BoardId}", new UpdateBoard.Request("Hijack", null, null, "#1D5BF5", current!.RowVersion));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Update_validates_input()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();

        var response = await client.PutAsJsonAsync(
            $"/api/boards/{boardId}", new UpdateBoard.Request("", null, new string('x', 501), "red", "not base64!"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Members_see_the_board_with_their_role_and_member_count()
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var asViewer = await board.Viewer.GetFromJsonAsync<GetBoard.Response>($"/api/boards/{board.BoardId}");
        var listed = await board.Editor.GetFromJsonAsync<List<GetMyBoards.Item>>("/api/boards");

        Assert.Equal(Domain.BoardRole.Viewer, asViewer!.Role);
        Assert.Equal(3, asViewer.MemberCount);
        Assert.Equal(Domain.BoardRole.Editor, Assert.Single(listed!).Role);
    }
}
