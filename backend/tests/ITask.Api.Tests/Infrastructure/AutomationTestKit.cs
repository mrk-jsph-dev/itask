using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ITask.Api.Common.Automations;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>Automation helpers that go through the public API.</summary>
public static class AutomationTestKit
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Automations(Guid boardId) => $"/api/boards/{boardId}/automations";

    public static Task<HttpResponseMessage> PostAutomationAsync(this HttpClient client, Guid boardId, object body) =>
        client.PostAsJsonAsync(Automations(boardId), body);

    public static async Task<AutomationView> CreateAutomationAsync(this HttpClient client, Guid boardId, object body)
    {
        var response = await client.PostAutomationAsync(boardId, body);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<AutomationView>())!;
    }

    /// <summary>A rule: when <paramref name="trigger"/>, do <paramref name="actions"/>.</summary>
    public static object Rule(object trigger, object[] actions, object[]? conditions = null, string name = "Test rule") =>
        new { kind = "Rule", name, trigger, conditions = conditions ?? [], actions };

    public static object Doorway(Guid listId, string direction, string mode, object[] requirements, object[]? actions = null, string name = "Gate") =>
        new { kind = "Doorway", name, listId, direction, mode, conditions = requirements, actions = actions ?? [] };

    /// <summary>The runs a response reports in the automations header (none when it is missing).</summary>
    public static List<AutomationRunNotice> Runs(this HttpResponseMessage response) =>
        response.Headers.TryGetValues(AutomationRunLog.Header, out var values)
            ? JsonSerializer.Deserialize<List<AutomationRunNotice>>(Encoding.UTF8.GetString(Convert.FromBase64String(values.Single())), Json)!
            : [];
}
