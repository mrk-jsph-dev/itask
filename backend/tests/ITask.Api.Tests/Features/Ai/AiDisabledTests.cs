using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Ai.GetAiStatus;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Ai;

public sealed class AiDisabledTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task With_ai_off_status_is_disabled_and_every_ai_route_is_404()
    {
        var client = await factory.CreateSignedInClientAsync();
        var boardId = await client.CreateBoardAsync();
        var list = (await client.GetListsAsync(boardId))[0].Id;
        var card = await client.CreateCardAsync(boardId, list, "Card");

        var status = await client.GetFromJsonAsync<GetAiStatus.Response>("/api/ai/status");
        Assert.Equal(new GetAiStatus.Response(false, 0, 0), status);

        string[] routes =
        [
            $"/api/boards/{boardId}/ai/quick-add",
            $"/api/boards/{boardId}/ai/digest",
            $"/api/boards/{boardId}/cards/{card.Id}/ai/breakdown",
            $"/api/boards/{boardId}/cards/{card.Id}/ai/summary",
            "/api/ai/board-from-goal",
        ];
        foreach (var route in routes)
        {
            var response = await client.PostAsJsonAsync(route, new { text = "x", goal = "x" });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
