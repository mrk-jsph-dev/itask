using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Domain;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>A board with members in each role and one card in its first list.</summary>
public sealed record CardOnBoard(BoardTestKit.BoardWithMembers Board, Guid CardId)
{
    public Guid BoardId => Board.BoardId;

    public string Url => $"{CardTestKit.Cards(BoardId)}/{CardId}";

    public static async Task<CardOnBoard> CreateAsync(ApiFactory factory)
    {
        var board = await factory.CreateBoardWithMembersAsync();
        var todo = (await board.Owner.GetListsAsync(board.BoardId))[0].Id;
        var card = await board.Owner.CreateCardAsync(board.BoardId, todo, "Write spec");
        return new CardOnBoard(board, card.Id);
    }

    public async Task<ChecklistItemView> AddItemAsync(HttpClient client, string text)
    {
        var response = await client.PostAsJsonAsync($"{Url}/checklist", new { text });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ChecklistItemView>())!;
    }

    public async Task<CardSummary> SummaryAsync() =>
        (await Board.Owner.GetListsAsync(BoardId)).SelectMany(l => l.Cards).Single(c => c.Id == CardId);

    public Task ArchiveBoardAsync() => Board.Owner.PostEmptyAsync($"/api/boards/{BoardId}/archive");

    public static BoardRole[] Roles => [BoardRole.Owner, BoardRole.Editor, BoardRole.Viewer];
}
