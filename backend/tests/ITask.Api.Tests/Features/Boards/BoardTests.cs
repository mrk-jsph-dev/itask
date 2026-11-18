using System.Net;
using System.Net.Http.Json;
using ITask.Api.Domain;
using ITask.Api.Features.Boards.CreateBoard;
using ITask.Api.Features.Boards.GetMyBoards;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Boards;

public sealed class BoardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Anonymous_users_cannot_list_boards()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.GetAsync(new Uri("/api/boards", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Creator_becomes_owner_and_sees_the_board()
    {
        var client = await factory.CreateSignedInClientAsync();

        var create = await client.PostAsJsonAsync("/api/boards", new CreateBoard.Request("Website relaunch", "🚀"));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var boards = await client.GetFromJsonAsync<List<GetMyBoards.Item>>("/api/boards");
        var board = Assert.Single(boards!);
        Assert.Equal("Website relaunch", board.Name);
        Assert.Equal(BoardRole.Owner, board.Role);
    }

    [Fact]
    public async Task Non_members_get_not_found()
    {
        var owner = await factory.CreateSignedInClientAsync();
        var stranger = await factory.CreateSignedInClientAsync();

        var create = await owner.PostAsJsonAsync("/api/boards", new CreateBoard.Request("Private plans", null));
        var created = await create.Content.ReadFromJsonAsync<CreateBoard.Response>();

        var response = await stranger.GetAsync(new Uri($"/api/boards/{created!.Id}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Empty_name_is_rejected()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/boards", new CreateBoard.Request("", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
