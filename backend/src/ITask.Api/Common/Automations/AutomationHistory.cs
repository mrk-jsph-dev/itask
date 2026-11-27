using System.Text.Json;
using ITask.Api.Common.Activity;
using ITask.Api.Domain;

namespace ITask.Api.Common.Automations;

/// <summary>A past card change read back from an activity entry, in automation terms.</summary>
public sealed record HistoryEvent(Guid CardId, Guid ActorId, DateTimeOffset At, string Type, Guid? ListId = null, Guid? LabelId = null);

/// <summary>
/// Turns activity entries back into the events automations react to. Entries snapshot names, so
/// lists and labels are matched by name against the board as it is now; renamed or deleted ones
/// drop out. Entries written by automations are skipped.
/// </summary>
public static class AutomationHistory
{
    /// <summary>Activity types that can map to an automation event.</summary>
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        ActivityTypes.CardCreated, ActivityTypes.CardMoved, ActivityTypes.CardCompleted, ActivityTypes.CardReopened,
        ActivityTypes.CardAssigneesChanged, ActivityTypes.CardLabelsChanged, ActivityTypes.ChecklistChecked,
        ActivityTypes.CommentAdded,
    };

    public static IEnumerable<HistoryEvent> Events(ActivityEntry entry, BoardContext board)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(board);
        if (entry.CardId is not { } cardId || AutomationMarker.Has(entry.Data))
        {
            yield break;
        }

        using var json = JsonDocument.Parse(entry.Data);
        var data = json.RootElement;
        HistoryEvent Event(string type, Guid? listId = null, Guid? labelId = null) =>
            new(cardId, entry.ActorId, entry.CreatedAt, type, listId, labelId);

        switch (entry.Type)
        {
            case ActivityTypes.CardCreated when ListId(board, data, "list") is { } created:
                yield return Event(AutomationCatalog.CardCreated, created);
                yield return Event(AutomationCatalog.CardEntered, created);
                break;
            case ActivityTypes.CardMoved:
                if (ListId(board, data, "fromList") is { } from)
                {
                    yield return Event(AutomationCatalog.CardLeft, from);
                }

                if (ListId(board, data, "toList") is { } to)
                {
                    yield return Event(AutomationCatalog.CardEntered, to);
                }

                break;
            case ActivityTypes.CardCompleted:
                yield return Event(AutomationCatalog.CardCompleted);
                break;
            case ActivityTypes.CardReopened:
                yield return Event(AutomationCatalog.CardReopened);
                break;
            case ActivityTypes.CardAssigneesChanged:
                yield return Event(AutomationCatalog.AssigneesChanged);
                break;
            case ActivityTypes.CommentAdded:
                yield return Event(AutomationCatalog.CommentAdded);
                break;
            case ActivityTypes.CardUnblocked:
                yield return Event(AutomationCatalog.CardUnblocked);
                break;
            case ActivityTypes.ChecklistChecked:
                yield return Event(AutomationCatalog.ChecklistDone);
                break;
            case ActivityTypes.CardLabelsChanged:
                foreach (var id in LabelIds(board, data, "added"))
                {
                    yield return Event(AutomationCatalog.LabelAdded, labelId: id);
                }

                foreach (var id in LabelIds(board, data, "removed"))
                {
                    yield return Event(AutomationCatalog.LabelRemoved, labelId: id);
                }

                break;
        }
    }

    public static bool Matches(TriggerSpec trigger, HistoryEvent e)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(e);
        return trigger.Type == e.Type
            && (trigger.ListId is null || trigger.ListId == e.ListId)
            && (trigger.LabelId is null || trigger.LabelId == e.LabelId);
    }

    private static Guid? ListId(BoardContext board, JsonElement data, string property) =>
        data.TryGetProperty(property, out var name) && name.ValueKind == JsonValueKind.String
            ? board.Lists.FirstOrDefault(l => string.Equals(l.Value, name.GetString(), StringComparison.OrdinalIgnoreCase)).Key is var id && id != Guid.Empty ? id : null
            : null;

    private static List<Guid> LabelIds(BoardContext board, JsonElement data, string property)
    {
        if (!data.TryGetProperty(property, out var names) || names.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return names.EnumerateArray()
            .Where(n => n.ValueKind == JsonValueKind.String)
            .Select(n => board.Labels.FirstOrDefault(l => string.Equals(l.Value, n.GetString(), StringComparison.OrdinalIgnoreCase)).Key)
            .Where(id => id != Guid.Empty)
            .ToList();
    }
}
