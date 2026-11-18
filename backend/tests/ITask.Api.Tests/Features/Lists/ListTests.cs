using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Lists.CreateList;
using ITask.Api.Features.Lists.GetBoardLists;
using ITask.Api.Features.Lists.MoveList;
using ITask.Api.Features.Lists.RenameList;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Tests.Features.Lists;

public sealed class ListTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Lists(Guid boardId) => $"/api/boards/{boardId}/lists";

    private static async Task<List<GetBoardLists.Item>> GetListsAsync(HttpClient client, Guid boardId, bool archived = false) =>
        (await client.GetFromJsonAsync<List<GetBoardLists.Item>>(Lists(boardId) + (archived ? "?archived=true" : "")))!;

    private static async Task<string[]> NamesAsync(HttpClient client, Guid boardId) =>
        [.. (await GetListsAsync(client, boardId)).Select(l => l.Name)];

    private static async Task<CreateList.Response> CreateAsync(HttpClient client, Guid boardId, string name)
    {
        var response = await client.PostAsJsonAsync(Lists(boardId), new CreateList.Request(name, "🧪"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateList.Response>())!;
    }

    private static Task<HttpResponseMessage> MoveAsync(HttpClient client, Guid boardId, Guid listId, Guid? beforeId, Guid? afterId) =>
        client.PutAsJsonAsync($"{Lists(boardId)}/{listId}/position", new MoveList.Request(beforeId, afterId));

    [Fact]
    public async Task New_boards_start_with_the_default_lists()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();

        var lists = await GetListsAsync(client, boardId);

        Assert.Equal(["To do", "In progress", "Review", "Done"], lists.Select(l => l.Name));
        Assert.Equal(["📝", "🚧", "👀", "✅"], lists.Select(l => l.Emoji));
        Assert.Equal([1024d, 2048, 3072, 4096], lists.Select(l => l.Position));
    }

    [Fact]
    public async Task Create_appends_to_the_end()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();

        var created = await CreateAsync(client, boardId, "  Blocked  ");

        Assert.Equal("Blocked", created.Name);
        Assert.Equal(5120, created.Position);
        Assert.Equal("Blocked", (await NamesAsync(client, boardId))[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_a_blank_name(string name)
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();

        var response = await client.PostAsJsonAsync(Lists(boardId), new CreateList.Request(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_validates_length_and_wip_limit()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();

        var response = await client.PostAsJsonAsync(Lists(boardId), new CreateList.Request(new string('x', 61), null, 0));
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Name", problem!.Errors.Keys);
        Assert.Contains("WipLimit", problem.Errors.Keys);
    }

    [Fact]
    public async Task Names_are_unique_per_board_ignoring_case()
    {
        var client = await factory.CreateSignedInClientAsync();
        var first = await client.CreateBoardAsync();
        var second = await client.CreateBoardAsync("Other board");

        var duplicate = await client.PostAsJsonAsync(Lists(first), new CreateList.Request("to DO"));
        var otherBoard = await client.PostAsJsonAsync(Lists(second), new CreateList.Request("Blocked"));
        var sameNameElsewhere = await client.PostAsJsonAsync(Lists(first), new CreateList.Request("Blocked"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherBoard.StatusCode);
        Assert.Equal(HttpStatusCode.Created, sameNameElsewhere.StatusCode);
    }

    [Fact]
    public async Task Rename_changes_name_emoji_and_wip_limit()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var todo = (await GetListsAsync(client, boardId))[0];

        var response = await client.PutAsJsonAsync(
            $"{Lists(boardId)}/{todo.Id}", new RenameList.Request("Backlog", "📦", 5));
        response.EnsureSuccessStatusCode();

        var renamed = (await GetListsAsync(client, boardId))[0];
        Assert.Equal(("Backlog", "📦", 5), (renamed.Name, renamed.Emoji, renamed.WipLimit));
    }

    [Fact]
    public async Task Rename_to_another_lists_name_is_a_conflict_but_own_name_is_fine()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var todo = (await GetListsAsync(client, boardId))[0];

        var clash = await client.PutAsJsonAsync($"{Lists(boardId)}/{todo.Id}", new RenameList.Request("done"));
        var recase = await client.PutAsJsonAsync($"{Lists(boardId)}/{todo.Id}", new RenameList.Request("TO DO"));

        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recase.StatusCode);
    }

    [Fact]
    public async Task Rename_of_a_list_on_another_board_is_not_found()
    {
        var client = await factory.CreateSignedInClientAsync();
        var mine = await client.CreateBoardAsync();
        var other = await client.CreateBoardAsync("Other");
        var foreign = (await GetListsAsync(client, other))[0];

        var response = await client.PutAsJsonAsync($"{Lists(mine)}/{foreign.Id}", new RenameList.Request("Nope"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Move_between_to_front_and_to_end_persists_the_order()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var l = (await GetListsAsync(client, boardId)).ToDictionary(x => x.Name, x => x.Id);

        (await MoveAsync(client, boardId, l["Done"], l["To do"], l["In progress"])).EnsureSuccessStatusCode();
        Assert.Equal(["To do", "Done", "In progress", "Review"], await NamesAsync(client, boardId));

        (await MoveAsync(client, boardId, l["Review"], null, l["To do"])).EnsureSuccessStatusCode();
        Assert.Equal(["Review", "To do", "Done", "In progress"], await NamesAsync(client, boardId));

        (await MoveAsync(client, boardId, l["Review"], null, null)).EnsureSuccessStatusCode();
        Assert.Equal(["To do", "Done", "In progress", "Review"], await NamesAsync(client, boardId));

        (await MoveAsync(client, boardId, l["To do"], l["Review"], null)).EnsureSuccessStatusCode();
        Assert.Equal(["Done", "In progress", "Review", "To do"], await NamesAsync(client, boardId));
    }

    [Fact]
    public async Task Move_with_neighbours_that_are_not_adjacent_is_a_conflict()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var l = (await GetListsAsync(client, boardId)).ToDictionary(x => x.Name, x => x.Id);

        var stale = await MoveAsync(client, boardId, l["Done"], l["To do"], l["Review"]);
        var unknown = await MoveAsync(client, boardId, l["Done"], Guid.NewGuid(), null);
        var self = await MoveAsync(client, boardId, l["Done"], l["Done"], null);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode);
        Assert.Equal(["To do", "In progress", "Review", "Done"], await NamesAsync(client, boardId));
    }

    [Fact]
    public async Task Move_into_a_tiny_gap_renumbers_the_board()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var l = (await GetListsAsync(client, boardId)).ToDictionary(x => x.Name, x => x.Id);
        await factory.WithDbAsync(async db =>
        {
            await db.Lists.Where(x => x.Id == l["In progress"]).ExecuteUpdateAsync(s => s.SetProperty(x => x.Position, 1024.0000001));
        });

        (await MoveAsync(client, boardId, l["Done"], l["To do"], l["In progress"])).EnsureSuccessStatusCode();

        var lists = await GetListsAsync(client, boardId);
        Assert.Equal(["To do", "Done", "In progress", "Review"], lists.Select(x => x.Name));
        Assert.Equal([1024d, 2048, 3072, 4096], lists.Select(x => x.Position));
    }

    [Fact]
    public async Task Archive_hides_the_list_and_restore_puts_it_at_the_end()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var todo = (await GetListsAsync(client, boardId))[0];

        (await client.PostAsync(new Uri($"{Lists(boardId)}/{todo.Id}/archive", UriKind.Relative), null)).EnsureSuccessStatusCode();
        (await client.PostAsync(new Uri($"{Lists(boardId)}/{todo.Id}/archive", UriKind.Relative), null)).EnsureSuccessStatusCode();

        Assert.DoesNotContain("To do", await NamesAsync(client, boardId));
        var archived = Assert.Single(await GetListsAsync(client, boardId, archived: true));
        Assert.Equal(todo.Id, archived.Id);
        Assert.NotNull(archived.ArchivedAt);

        (await client.PostAsync(new Uri($"{Lists(boardId)}/{todo.Id}/restore", UriKind.Relative), null)).EnsureSuccessStatusCode();

        Assert.Equal(["In progress", "Review", "Done", "To do"], await NamesAsync(client, boardId));
        Assert.Empty(await GetListsAsync(client, boardId, archived: true));
    }

    [Fact]
    public async Task Archived_names_are_free_and_restoring_a_clash_is_a_conflict()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var todo = (await GetListsAsync(client, boardId))[0];
        (await client.PostAsync(new Uri($"{Lists(boardId)}/{todo.Id}/archive", UriKind.Relative), null)).EnsureSuccessStatusCode();

        await CreateAsync(client, boardId, "To do");
        var restore = await client.PostAsync(new Uri($"{Lists(boardId)}/{todo.Id}/restore", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Conflict, restore.StatusCode);
    }

    [Fact]
    public async Task Lists_on_an_archived_board_are_read_only()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        (await client.PostAsync(new Uri($"/api/boards/{boardId}/archive", UriKind.Relative), null)).EnsureSuccessStatusCode();

        var create = await client.PostAsJsonAsync(Lists(boardId), new CreateList.Request("Late"));

        Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        Assert.Equal(4, (await GetListsAsync(client, boardId)).Count);
    }

    [Theory]
    [InlineData("Owner", HttpStatusCode.OK)]
    [InlineData("Editor", HttpStatusCode.OK)]
    [InlineData("Viewer", HttpStatusCode.OK)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Members_read_lists_and_strangers_get_404(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var response = await board.For(role).GetAsync(new Uri(Lists(board.BoardId), UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.Created)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_editors_create(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var response = await board.For(role).PostAsJsonAsync(Lists(board.BoardId), new CreateList.Request("Ideas"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.OK)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_editors_rename(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var todo = (await GetListsAsync(board.Owner, board.BoardId))[0];

        var response = await board.For(role).PutAsJsonAsync(
            $"{Lists(board.BoardId)}/{todo.Id}", new RenameList.Request("Backlog"));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.OK)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_editors_move(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var lists = await GetListsAsync(board.Owner, board.BoardId);

        var response = await MoveAsync(board.For(role), board.BoardId, lists[3].Id, null, lists[0].Id);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.NoContent)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_editors_archive(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var todo = (await GetListsAsync(board.Owner, board.BoardId))[0];

        var response = await board.For(role).PostAsync(
            new Uri($"{Lists(board.BoardId)}/{todo.Id}/archive", UriKind.Relative), null);

        Assert.Equal(expected, response.StatusCode);
    }
}
