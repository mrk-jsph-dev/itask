using System.Globalization;
using System.Text.Json;
using ITask.Api.Domain;

namespace ITask.Api.Infrastructure.Ai;

/// <summary>Thrown when the model's answer is not usable JSON for the requested draft.</summary>
public sealed class AiAnswerException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record QuickAddAnswer(string Title, DateOnly? DueDate, CardPriority Priority, IReadOnlyList<string> Labels, string? List);

public sealed record BoardFromGoalAnswer(string Name, string? Emoji, IReadOnlyList<string> Lists, IReadOnlyList<GoalCardAnswer> Cards);

public sealed record GoalCardAnswer(string List, string Title, IReadOnlyList<string> Checklist);

/// <summary>
/// JSON schemas for structured outputs and the parsers that validate a model answer before
/// it is returned as a draft: text is trimmed and cut to the domain limits, names are kept only
/// when they match the board, counts are capped.
/// </summary>
public static class AiDrafts
{
    public const int MaxBreakdownItems = 12;

    public const int MaxGoalLists = 8;

    public const int MaxGoalCards = 20;

    public const int MaxGoalChecklist = 8;

    private const int GoalCardTitleMax = 100;

    public static readonly BinaryData QuickAddSchema = BinaryData.FromString("""
        {
          "type": "object",
          "properties": {
            "title": { "type": "string" },
            "dueDate": { "type": ["string", "null"], "description": "YYYY-MM-DD" },
            "priority": { "type": "string", "enum": ["None", "Low", "Medium", "High", "Urgent"] },
            "labels": { "type": "array", "items": { "type": "string" } },
            "list": { "type": ["string", "null"] }
          },
          "required": ["title", "dueDate", "priority", "labels", "list"],
          "additionalProperties": false
        }
        """);

    public static readonly BinaryData BreakdownSchema = BinaryData.FromString("""
        {
          "type": "object",
          "properties": { "items": { "type": "array", "items": { "type": "string" } } },
          "required": ["items"],
          "additionalProperties": false
        }
        """);

    public static readonly BinaryData BoardFromGoalSchema = BinaryData.FromString("""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string" },
            "emoji": { "type": "string" },
            "lists": { "type": "array", "items": { "type": "string" } },
            "cards": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "list": { "type": "string" },
                  "title": { "type": "string" },
                  "checklist": { "type": "array", "items": { "type": "string" } }
                },
                "required": ["list", "title", "checklist"],
                "additionalProperties": false
              }
            }
          },
          "required": ["name", "emoji", "lists", "cards"],
          "additionalProperties": false
        }
        """);

    /// <param name="labels">The board's label names; others are dropped.</param>
    /// <param name="lists">The board's active list names; an unknown list becomes null.</param>
    public static QuickAddAnswer ParseQuickAdd(string json, IReadOnlyCollection<string> labels, IReadOnlyCollection<string> lists)
    {
        using var doc = Parse(json);
        var root = doc.RootElement;
        var title = Clip(String(root, "title"), Card.TitleMaxLength)
            ?? throw new AiAnswerException("The answer has no title.");

        DateOnly? due = null;
        if (String(root, "dueDate") is { } dueText
            && DateOnly.TryParseExact(dueText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            due = parsed;
        }

        var priority = Enum.TryParse<CardPriority>(String(root, "priority"), ignoreCase: true, out var p) && Enum.IsDefined(p)
            ? p
            : CardPriority.None;

        var labelNames = Strings(root, "labels")
            .Select(name => labels.FirstOrDefault(l => string.Equals(l, name, StringComparison.OrdinalIgnoreCase)))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Card.MaxLabelsOrAssignees)
            .ToList();

        var list = String(root, "list") is { } listName
            ? lists.FirstOrDefault(l => string.Equals(l, listName, StringComparison.OrdinalIgnoreCase))
            : null;

        return new QuickAddAnswer(title, due, priority, labelNames, list);
    }

    /// <param name="existing">Items already on the card; repeats are dropped.</param>
    public static IReadOnlyList<string> ParseBreakdown(string json, IReadOnlyCollection<string> existing)
    {
        using var doc = Parse(json);
        var items = Strings(doc.RootElement, "items")
            .Select(i => Clip(i, ChecklistItem.TextMaxLength))
            .OfType<string>()
            .Where(i => !existing.Contains(i, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxBreakdownItems)
            .ToList();
        return items.Count > 0 ? items : throw new AiAnswerException("The answer has no checklist items.");
    }

    public static BoardFromGoalAnswer ParseBoardFromGoal(string json)
    {
        using var doc = Parse(json);
        var root = doc.RootElement;
        var name = Clip(String(root, "name"), 120) ?? throw new AiAnswerException("The answer has no board name.");
        var emoji = Clip(String(root, "emoji"), 16);

        var lists = Strings(root, "lists")
            .Select(l => Clip(l, BoardList.NameMaxLength))
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxGoalLists)
            .ToList();
        if (lists.Count == 0)
        {
            throw new AiAnswerException("The answer has no lists.");
        }

        var cards = new List<GoalCardAnswer>();
        if (root.TryGetProperty("cards", out var cardArray) && cardArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var card in cardArray.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object))
            {
                var list = lists.FirstOrDefault(l => string.Equals(l, String(card, "list"), StringComparison.OrdinalIgnoreCase));
                if (list is null || Clip(String(card, "title"), GoalCardTitleMax) is not { } title)
                {
                    continue;
                }

                var checklist = Strings(card, "checklist")
                    .Select(s => Clip(s, ChecklistItem.TextMaxLength))
                    .OfType<string>()
                    .Take(MaxGoalChecklist)
                    .ToList();
                cards.Add(new GoalCardAnswer(list, title, checklist));
                if (cards.Count == MaxGoalCards)
                {
                    break;
                }
            }
        }

        return new BoardFromGoalAnswer(name, emoji, lists, cards);
    }

    /// <summary>Plain-text answers (summary, digest): trimmed and cut to <paramref name="max"/> characters.</summary>
    public static string ParseText(string text, int max) =>
        Clip(text, max) ?? throw new AiAnswerException("The answer is empty.");

    private static JsonDocument Parse(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                doc.Dispose();
                throw new AiAnswerException("The answer is not a JSON object.");
            }

            return doc;
        }
        catch (JsonException e)
        {
            throw new AiAnswerException("The answer is not valid JSON.", e);
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IEnumerable<string> Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)
            : [];

    private static string? Clip(string? text, int max)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= max ? trimmed : trimmed[..max].TrimEnd();
    }
}
