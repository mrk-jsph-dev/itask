using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Cards.MoveCard;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Tests.Features.Cards;

public sealed class CardMoveTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Setup(HttpClient Client, Guid BoardId, Dictionary<string, Guid> Lists);

    private async Task<Setup> BoardWithCardsAsync(params string[] todoCards)
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var lists = (await client.GetListsAsync(boardId)).ToDictionary(l => l.Name, l => l.Id);
        foreach (var title in todoCards)
        {
            await client.CreateCardAsync(boardId, lists["To do"], title);
        }

        return new Setup(client, boardId, lists);
    }

    private static async Task<Dictionary<string, ITask.Api.Common.Cards.CardSummary>> CardsAsync(Setup s) =>
        (await s.Client.GetListsAsync(s.BoardId)).SelectMany(l => l.Cards).ToDictionary(c => c.Title);

    [Fact]
    public async Task Move_within_a_list_takes_the_midpoint()
    {
        var s = await BoardWithCardsAsync("A", "B", "C");
        var cards = await CardsAsync(s);

        var response = await s.Client.MoveCardAsync(s.BoardId, cards["C"], s.Lists["To do"], cards["A"].Id, cards["B"].Id);
        var moved = (await response.Content.ReadFromJsonAsync<MoveCard.Response>())!;

        Assert.Equal(1536, moved.Card.Position);
        Assert.False(moved.Renumbered);
        Assert.Equal(["A", "C", "B"], (await s.Client.CardTitlesAsync(s.BoardId))["To do"]);
    }

    [Fact]
    public async Task Move_across_lists_recalculates_the_position()
    {
        var s = await BoardWithCardsAsync("A", "B");
        var cards = await CardsAsync(s);
        var doing = s.Lists["In progress"];

        (await s.Client.MoveCardAsync(s.BoardId, cards["A"], doing)).EnsureSuccessStatusCode();
        var response = await s.Client.MoveCardAsync(s.BoardId, cards["B"], doing, null, cards["A"].Id);
        var moved = (await response.Content.ReadFromJsonAsync<MoveCard.Response>())!;

        Assert.Equal(512, moved.Card.Position);
        Assert.Equal(doing, moved.Card.ListId);
        var titles = await s.Client.CardTitlesAsync(s.BoardId);
        Assert.Empty(titles["To do"]);
        Assert.Equal(["B", "A"], titles["In progress"]);
    }

    [Fact]
    public async Task Concurrent_move_with_a_stale_row_version_is_a_conflict()
    {
        var s = await BoardWithCardsAsync("A", "B");
        var stale = (await CardsAsync(s))["A"];

        (await s.Client.MoveCardAsync(s.BoardId, stale, s.Lists["Done"])).EnsureSuccessStatusCode();
        var second = await s.Client.MoveCardAsync(s.BoardId, stale, s.Lists["Review"]);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(["A"], (await s.Client.CardTitlesAsync(s.BoardId))["Done"]);
    }

    [Fact]
    public async Task Move_with_neighbours_that_are_not_adjacent_is_a_conflict()
    {
        var s = await BoardWithCardsAsync("A", "B", "C", "D");
        var cards = await CardsAsync(s);

        var response = await s.Client.MoveCardAsync(s.BoardId, cards["D"], s.Lists["To do"], cards["A"].Id, cards["C"].Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Move_next_to_a_card_in_another_list_is_a_conflict()
    {
        var s = await BoardWithCardsAsync("A", "B");
        var cards = await CardsAsync(s);

        var response = await s.Client.MoveCardAsync(s.BoardId, cards["A"], s.Lists["Done"], cards["B"].Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Move_into_a_tiny_gap_renumbers_the_list()
    {
        var s = await BoardWithCardsAsync("A", "B", "C");
        var cards = await CardsAsync(s);
        await factory.WithDbAsync(async db =>
        {
            var id = cards["B"].Id;
            await db.Cards.Where(c => c.Id == id).ExecuteUpdateAsync(u => u.SetProperty(c => c.Position, 1024.0000001));
        });

        var response = await s.Client.MoveCardAsync(s.BoardId, cards["C"], s.Lists["To do"], cards["A"].Id, cards["B"].Id);
        var moved = (await response.Content.ReadFromJsonAsync<MoveCard.Response>())!;

        Assert.True(moved.Renumbered);
        var todo = (await s.Client.GetListsAsync(s.BoardId)).Single(l => l.Name == "To do").Cards;
        Assert.Equal(["A", "C", "B"], todo.Select(c => c.Title));
        Assert.Equal([1024d, 2048, 3072], todo.Select(c => c.Position));
    }

    [Fact]
    public async Task Move_to_an_archived_list_is_a_conflict()
    {
        var s = await BoardWithCardsAsync("A");
        var card = (await CardsAsync(s))["A"];
        await s.Client.PostEmptyAsync($"/api/boards/{s.BoardId}/lists/{s.Lists["Done"]}/archive");

        var response = await s.Client.MoveCardAsync(s.BoardId, card, s.Lists["Done"]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Move_validates_the_row_version()
    {
        var s = await BoardWithCardsAsync("A");
        var card = (await CardsAsync(s))["A"] with { RowVersion = "not base64!" };

        var response = await s.Client.MoveCardAsync(s.BoardId, card, s.Lists["Done"]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("Editor", HttpStatusCode.OK)]
    [InlineData("Viewer", HttpStatusCode.Forbidden)]
    [InlineData("Stranger", HttpStatusCode.NotFound)]
    public async Task Only_editors_move(string role, HttpStatusCode expected)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var lists = await board.Owner.GetListsAsync(board.BoardId);
        var card = await board.Owner.CreateCardAsync(board.BoardId, lists[0].Id, "A");

        var response = await board.For(role).MoveCardAsync(board.BoardId, card, lists[1].Id);

        Assert.Equal(expected, response.StatusCode);
    }
}
