using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Features.Cards.SetCardLabels;
using ITask.Api.Features.Labels.CreateLabel;
using ITask.Api.Features.Labels.UpdateLabel;
using ITask.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ITask.Api.Tests.Features.Labels;

public sealed class LabelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static string Labels(Guid boardId) => $"/api/boards/{boardId}/labels";

    private static async Task<LabelChip> CreateAsync(HttpClient client, Guid boardId, string name, string color = "#1D5BF5")
    {
        var response = await client.PostAsJsonAsync(Labels(boardId), new CreateLabel.Request(name, color, "🐞"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LabelChip>())!;
    }

    [Fact]
    public async Task Create_list_update_and_delete()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();

        var bug = await CreateAsync(client, boardId, "  Bug ", "#e11d48");
        await CreateAsync(client, boardId, "backend");
        var updated = await client.PutAsJsonAsync($"{Labels(boardId)}/{bug.Id}", new UpdateLabel.Request("Defect", "#B91C1C"));
        updated.EnsureSuccessStatusCode();
        var afterUpdate = await client.GetFromJsonAsync<List<LabelChip>>(Labels(boardId));
        var deleted = await client.DeleteAsync(new Uri($"{Labels(boardId)}/{bug.Id}", UriKind.Relative));

        Assert.Equal(("Bug", "#E11D48", "🐞"), (bug.Name, bug.Color, bug.Emoji));
        Assert.Equal([("backend", "#1D5BF5"), ("Defect", "#B91C1C")], afterUpdate!.Select(l => (l.Name, l.Color)));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(["backend"], (await client.GetFromJsonAsync<List<LabelChip>>(Labels(boardId)))!.Select(l => l.Name));
    }

    [Fact]
    public async Task Names_are_unique_per_board_ignoring_case()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var other = await client.CreateBoardAsync("Other");
        await CreateAsync(client, boardId, "Bug");
        var frontend = await CreateAsync(client, boardId, "Frontend");

        var duplicate = await client.PostAsJsonAsync(Labels(boardId), new CreateLabel.Request("BUG", "#1D5BF5"));
        var rename = await client.PutAsJsonAsync($"{Labels(boardId)}/{frontend.Id}", new UpdateLabel.Request("bug", "#1D5BF5"));
        var elsewhere = await client.PostAsJsonAsync(Labels(other), new CreateLabel.Request("Bug", "#1D5BF5"));

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode);
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
    }

    [Fact]
    public async Task Create_validates_name_and_colour()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();

        var response = await client.PostAsJsonAsync(Labels(boardId), new CreateLabel.Request(new string('x', 31), "red"));
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Name", problem!.Errors.Keys);
        Assert.Contains("Color", problem.Errors.Keys);
    }

    [Fact]
    public async Task Cards_take_only_this_boards_labels_and_deleting_a_label_unlinks_it()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var other = await client.CreateBoardAsync("Other");
        var bug = await CreateAsync(client, boardId, "Bug");
        var ui = await CreateAsync(client, boardId, "UI");
        var foreign = await CreateAsync(client, other, "Bug");
        var todo = (await client.GetListsAsync(boardId))[0].Id;
        var card = await client.CreateCardAsync(boardId, todo, "Fix login");
        var url = $"{CardTestKit.Cards(boardId)}/{card.Id}/labels";

        var rejected = await client.PutAsJsonAsync(url, new SetCardLabels.Request([bug.Id, foreign.Id]));
        var set = await client.PutAsJsonAsync(url, new SetCardLabels.Request([ui.Id, bug.Id]));
        var labelled = (await set.Content.ReadFromJsonAsync<CardSummary>())!;
        await client.DeleteAsync(new Uri($"{Labels(boardId)}/{bug.Id}", UriKind.Relative));
        var after = (await client.GetListsAsync(boardId))[0].Cards.Single();

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(["Bug", "UI"], labelled.Labels.Select(l => l.Name));
        Assert.Equal(["UI"], after.Labels.Select(l => l.Name));
    }

    [Theory]
    [InlineData("Viewer", HttpStatusCode.OK)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Members_read_labels(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();

        var response = await board.For(role).GetAsync(new Uri(Labels(board.BoardId), UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.Created, HttpStatusCode.OK, HttpStatusCode.NoContent)]
    [InlineData("Viewer", HttpStatusCode.Forbidden, HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound)]
    public async Task Only_editors_change_labels(string role, HttpStatusCode create, HttpStatusCode update, HttpStatusCode delete)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var label = await CreateAsync(board.Owner, board.BoardId, "Bug");
        var client = board.For(role);

        var created = await client.PostAsJsonAsync(Labels(board.BoardId), new CreateLabel.Request("UI", "#1D5BF5"));
        var updated = await client.PutAsJsonAsync($"{Labels(board.BoardId)}/{label.Id}", new UpdateLabel.Request("Defect", "#1D5BF5"));
        var deleted = await client.DeleteAsync(new Uri($"{Labels(board.BoardId)}/{label.Id}", UriKind.Relative));

        Assert.Equal((create, update, delete), (created.StatusCode, updated.StatusCode, deleted.StatusCode));
    }
}
