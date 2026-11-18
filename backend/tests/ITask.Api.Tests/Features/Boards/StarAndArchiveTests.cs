using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Boards.GetMyBoards;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Boards;

public sealed class StarAndArchiveTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Uri Url(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Stars_are_per_user_and_filter_the_list()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var other = await board.Owner.CreateBoardAsync("Other");

        Assert.Equal(HttpStatusCode.NoContent, (await board.Viewer.PutAsync(Url($"/api/boards/{board.BoardId}/star"), null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await board.Viewer.PutAsync(Url($"/api/boards/{board.BoardId}/star"), null)).StatusCode);

        var viewerStarred = await board.Viewer.GetFromJsonAsync<List<GetMyBoards.Item>>("/api/boards?starred=true");
        var ownerStarred = await board.Owner.GetFromJsonAsync<List<GetMyBoards.Item>>("/api/boards?starred=true");
        var ownerAll = await board.Owner.GetFromJsonAsync<List<GetMyBoards.Item>>("/api/boards");

        Assert.True(Assert.Single(viewerStarred!).IsStarred);
        Assert.Empty(ownerStarred!);
        Assert.Equal(2, ownerAll!.Count);
        Assert.Contains(ownerAll, b => b.Id == other);

        await board.Viewer.DeleteAsync(Url($"/api/boards/{board.BoardId}/star"));
        Assert.Empty((await board.Viewer.GetFromJsonAsync<List<GetMyBoards.Item>>("/api/boards?starred=true"))!);
    }

    [Fact]
    public async Task Strangers_cannot_star()
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var response = await board.Stranger.PutAsync(Url($"/api/boards/{board.BoardId}/star"), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Archived_boards_move_to_the_archived_list_and_back()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(Url($"/api/boards/{boardId}/archive"), null)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<GetMyBoards.Item>>("/api/boards"))!);
        var archived = Assert.Single((await client.GetFromJsonAsync<List<GetMyBoards.Item>>("/api/boards?archived=true"))!);
        Assert.NotNull(archived.ArchivedAt);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync(Url($"/api/boards/{boardId}/restore"), null)).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<List<GetMyBoards.Item>>("/api/boards"))!);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.Forbidden)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_owners_archive_and_delete(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var archive = await board.For(role).PostAsync(Url($"/api/boards/{board.BoardId}/archive"), null);
        await board.Owner.PostAsync(Url($"/api/boards/{board.BoardId}/archive"), null);
        var delete = await board.For(role).DeleteAsync(Url($"/api/boards/{board.BoardId}"));

        Assert.Equal(expected, archive.StatusCode);
        Assert.Equal(expected, delete.StatusCode);
    }

    [Fact]
    public async Task Delete_requires_an_archived_board()
    {
        var board = await factory.CreateBoardWithMembersAsync();
        await board.Viewer.PutAsync(Url($"/api/boards/{board.BoardId}/star"), null);

        Assert.Equal(HttpStatusCode.Conflict, (await board.Owner.DeleteAsync(Url($"/api/boards/{board.BoardId}"))).StatusCode);

        await board.Owner.PostAsync(Url($"/api/boards/{board.BoardId}/archive"), null);
        Assert.Equal(HttpStatusCode.NoContent, (await board.Owner.DeleteAsync(Url($"/api/boards/{board.BoardId}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await board.Viewer.GetAsync(Url($"/api/boards/{board.BoardId}"))).StatusCode);
    }
}
