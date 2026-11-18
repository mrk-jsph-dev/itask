using System.Net.Http.Json;
using ITask.Api.Common.Cards;
using ITask.Api.Features.Cards.CreateCard;
using ITask.Api.Features.Cards.MoveCard;
using ITask.Api.Features.Lists.GetBoardLists;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>Card helpers that go through the public API.</summary>
public static class CardTestKit
{
    public static string Cards(Guid boardId) => $"/api/boards/{boardId}/cards";

    public static async Task<List<GetBoardLists.Item>> GetListsAsync(this HttpClient client, Guid boardId) =>
        (await client.GetFromJsonAsync<List<GetBoardLists.Item>>($"/api/boards/{boardId}/lists"))!;

    public static async Task<CardSummary> CreateCardAsync(this HttpClient client, Guid boardId, Guid listId, string title)
    {
        var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/lists/{listId}/cards", new CreateCard.Request(title));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CardSummary>())!;
    }

    public static Task<HttpResponseMessage> MoveCardAsync(
        this HttpClient client, Guid boardId, CardSummary card, Guid listId, Guid? beforeId = null, Guid? afterId = null) =>
        client.PutAsJsonAsync(
            $"{Cards(boardId)}/{card.Id}/position", new MoveCard.Request(listId, beforeId, afterId, card.RowVersion));

    public static Task<HttpResponseMessage> PostEmptyAsync(this HttpClient client, string url) =>
        client.PostAsync(new Uri(url, UriKind.Relative), null);

    /// <summary>Titles of each active list's cards, keyed by list name.</summary>
    public static async Task<Dictionary<string, string[]>> CardTitlesAsync(this HttpClient client, Guid boardId) =>
        (await client.GetListsAsync(boardId)).ToDictionary(l => l.Name, l => l.Cards.Select(c => c.Title).ToArray());
}
