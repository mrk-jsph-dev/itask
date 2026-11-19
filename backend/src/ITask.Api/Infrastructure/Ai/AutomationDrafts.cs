using System.Text.Json;
using ITask.Api.Common.Automations;
using ITask.Api.Domain;

namespace ITask.Api.Infrastructure.Ai;

/// <summary>An automation the model drafted, with ids resolved against the board. Never saved by the AI slices.</summary>
public sealed record AutomationDraft(
    string Name,
    AutomationKind Kind,
    Guid? ListId,
    DoorwayDirection? Direction,
    DoorwayMode? Mode,
    TriggerSpec Trigger,
    IReadOnlyList<ConditionSpec> Conditions,
    IReadOnlyList<ActionSpec> Actions);

public sealed record AutomationSuggestion(string Reason, AutomationDraft Automation);

/// <summary>JSON schemas and validation for AI-drafted automations. The model names lists, labels and members; ids are looked up here.</summary>
public static class AutomationDrafts
{
    public const int MaxSuggestions = 3;

    private const int NameMax = 40;

    private const string AutomationSchemaBody = """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string" },
            "kind": { "type": "string", "enum": ["Rule", "Doorway"] },
            "list": { "type": ["string", "null"] },
            "direction": { "type": ["string", "null"], "enum": ["Enter", "Leave", null] },
            "mode": { "type": ["string", "null"], "enum": ["Block", "Fix", "Warn", null] },
            "trigger": {
              "type": "object",
              "properties": {
                "type": { "type": "string" },
                "list": { "type": ["string", "null"] },
                "label": { "type": ["string", "null"] },
                "days": { "type": ["integer", "null"] }
              },
              "required": ["type", "list", "label", "days"],
              "additionalProperties": false
            },
            "conditions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "type": { "type": "string" },
                  "labels": { "type": "array", "items": { "type": "string" } },
                  "priority": { "type": ["string", "null"] },
                  "member": { "type": ["string", "null"] }
                },
                "required": ["type", "labels", "priority", "member"],
                "additionalProperties": false
              }
            },
            "actions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "type": { "type": "string" },
                  "list": { "type": ["string", "null"] },
                  "position": { "type": ["string", "null"] },
                  "label": { "type": ["string", "null"] },
                  "who": { "type": ["string", "null"] },
                  "member": { "type": ["string", "null"] },
                  "priority": { "type": ["string", "null"] },
                  "days": { "type": ["integer", "null"] },
                  "items": { "type": "array", "items": { "type": "string" } },
                  "text": { "type": ["string", "null"] }
                },
                "required": ["type", "list", "position", "label", "who", "member", "priority", "days", "items", "text"],
                "additionalProperties": false
              }
            }
          },
          "required": ["name", "kind", "list", "direction", "mode", "trigger", "conditions", "actions"],
          "additionalProperties": false
        }
        """;

    public static readonly BinaryData AutomationSchema = BinaryData.FromString(AutomationSchemaBody);

    public static readonly BinaryData ReviewSchema = BinaryData.FromString($$"""
        {
          "type": "object",
          "properties": {
            "suggestions": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "reason": { "type": "string" },
                  "automation": {{AutomationSchemaBody}}
                },
                "required": ["reason", "automation"],
                "additionalProperties": false
              }
            }
          },
          "required": ["suggestions"],
          "additionalProperties": false
        }
        """);

    public static AutomationDraft ParseAutomation(string json, BoardContext board)
    {
        ArgumentNullException.ThrowIfNull(board);
        using var doc = Parse(json);
        return Draft(doc.RootElement, board);
    }

    public static IReadOnlyList<AutomationSuggestion> ParseReview(string json, BoardContext board)
    {
        ArgumentNullException.ThrowIfNull(board);
        using var doc = Parse(json);
        var suggestions = new List<AutomationSuggestion>();
        if (doc.RootElement.TryGetProperty("suggestions", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray().Take(MaxSuggestions * 2))
            {
                try
                {
                    var reason = Clip(String(item, "reason"), 200) ?? "Suggested from this board's activity.";
                    if (item.TryGetProperty("automation", out var automation) && automation.ValueKind == JsonValueKind.Object)
                    {
                        suggestions.Add(new AutomationSuggestion(reason, Draft(automation, board)));
                    }
                }
                catch (AiAnswerException)
                {
                    // One unusable suggestion doesn't spoil the rest.
                }
            }
        }

        return suggestions.Count > 0
            ? [.. suggestions.Take(MaxSuggestions)]
            : throw new AiAnswerException("The answer has no usable suggestions.");
    }

    private static AutomationDraft Draft(JsonElement root, BoardContext board)
    {
        Guid? List(string? name) => Find(board.Lists, name);
        Guid? Label(string? name) => Find(board.Labels, name);
        Guid? Member(string? name) => name is null
            ? null
            : board.Members.FirstOrDefault(m => string.Equals(m.Value.Name, name, StringComparison.OrdinalIgnoreCase)).Key is var id && id != Guid.Empty ? id : null;

        var kind = Enum.TryParse<AutomationKind>(String(root, "kind"), out var k) ? k : AutomationKind.Rule;
        var listId = kind == AutomationKind.Doorway ? List(String(root, "list")) : null;
        var direction = kind == AutomationKind.Doorway && Enum.TryParse<DoorwayDirection>(String(root, "direction"), out var d) ? d : (DoorwayDirection?)null;
        var mode = kind == AutomationKind.Doorway && Enum.TryParse<DoorwayMode>(String(root, "mode"), out var m) ? m : (DoorwayMode?)null;

        var trigger = new TriggerSpec("");
        if (root.TryGetProperty("trigger", out var t) && t.ValueKind == JsonValueKind.Object)
        {
            trigger = new TriggerSpec(String(t, "type") ?? "", List(String(t, "list")), Label(String(t, "label")), Int(t, "days"));
        }

        var conditions = Objects(root, "conditions")
            .Select(c => new ConditionSpec(
                String(c, "type") ?? "",
                Strings(c, "labels").Select(Label).OfType<Guid>().ToList() is { Count: > 0 } ids ? ids : null,
                Enum.TryParse<CardPriority>(String(c, "priority"), out var p) ? p : null,
                Member(String(c, "member"))))
            .ToList();

        var actions = Objects(root, "actions")
            .Select(a => new ActionSpec(
                String(a, "type") ?? "",
                List(String(a, "list")),
                String(a, "position"),
                Label(String(a, "label")),
                String(a, "who"),
                Member(String(a, "member")),
                Enum.TryParse<CardPriority>(String(a, "priority"), out var p) ? p : null,
                Int(a, "days"),
                Strings(a, "items").Select(i => Clip(i, ChecklistItem.TextMaxLength)).OfType<string>().ToList() is { Count: > 0 } items ? items : null,
                Clip(String(a, "text"), AutomationCatalog.MaxCommentLength)))
            .Where(a => !AutomationCatalog.IsAi(a.Type))
            .ToList();

        var spec = new AutomationSpec(
            kind == AutomationKind.Doorway && listId is { } l && direction is { } dir ? AutomationSpec.DoorwayTrigger(l, dir) : trigger,
            conditions,
            actions);
        var errors = AutomationCatalog.Validate(kind, listId, direction, mode, spec, board);
        if (errors.Count > 0)
        {
            throw new AiAnswerException("The drafted automation isn't valid: " + string.Join(" ", errors.SelectMany(e => e.Value)));
        }

        var name = Clip(String(root, "name"), NameMax) ?? "Suggested automation";
        return new AutomationDraft(name, kind, listId, direction, mode, spec.Trigger, spec.Conditions, spec.Actions);
    }

    private static Guid? Find(IReadOnlyDictionary<Guid, string> items, string? name) =>
        name is null ? null : items.FirstOrDefault(i => string.Equals(i.Value, name, StringComparison.OrdinalIgnoreCase)).Key is var id && id != Guid.Empty ? id : null;

    private static JsonDocument Parse(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                return doc;
            }

            doc.Dispose();
            throw new AiAnswerException("The answer is not a JSON object.");
        }
        catch (JsonException e)
        {
            throw new AiAnswerException("The answer is not valid JSON.", e);
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) ? n : null;

    private static IEnumerable<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)
            : [];

    private static IEnumerable<JsonElement> Objects(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Clip(string? text, int max)
    {
        var trimmed = text?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= max ? trimmed : trimmed[..max].TrimEnd();
    }
}
