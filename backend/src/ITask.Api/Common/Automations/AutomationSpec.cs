using System.Text.Json;
using System.Text.Json.Serialization;
using ITask.Api.Domain;

namespace ITask.Api.Common.Automations;

/// <summary>What starts an automation. Which fields apply depends on <see cref="Type"/> (see <see cref="AutomationCatalog"/>).</summary>
public sealed record TriggerSpec(string Type, Guid? ListId = null, Guid? LabelId = null, int? Days = null, bool? Mentions = null);

/// <summary>A filter for rules, or a requirement for doorways.</summary>
public sealed record ConditionSpec(string Type, IReadOnlyList<Guid>? LabelIds = null, CardPriority? Priority = null, Guid? UserId = null);

/// <summary>A change an automation makes to the card it runs on.</summary>
public sealed record ActionSpec(
    string Type,
    Guid? ListId = null,
    string? Position = null,
    Guid? LabelId = null,
    string? Who = null,
    Guid? UserId = null,
    CardPriority? Priority = null,
    int? Days = null,
    IReadOnlyList<string>? Items = null,
    string? Text = null);

/// <summary>The parts of an automation, parsed from <see cref="AutomationRule"/>'s JSON columns.</summary>
public sealed record AutomationSpec(TriggerSpec Trigger, IReadOnlyList<ConditionSpec> Conditions, IReadOnlyList<ActionSpec> Actions)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AutomationSpec Of(AutomationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new AutomationSpec(
            JsonSerializer.Deserialize<TriggerSpec>(rule.Trigger, Json) ?? new TriggerSpec(""),
            JsonSerializer.Deserialize<List<ConditionSpec>>(rule.Conditions, Json) ?? [],
            JsonSerializer.Deserialize<List<ActionSpec>>(rule.Actions, Json) ?? []);
    }

    /// <summary>The trigger a doorway implies: entering or leaving its list.</summary>
    public static TriggerSpec DoorwayTrigger(Guid listId, DoorwayDirection direction) =>
        new(direction == DoorwayDirection.Enter ? AutomationCatalog.CardEntered : AutomationCatalog.CardLeft, listId);

    public void WriteTo(AutomationRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        rule.Trigger = JsonSerializer.Serialize(Trigger, Json);
        rule.Conditions = JsonSerializer.Serialize(Conditions, Json);
        rule.Actions = JsonSerializer.Serialize(Actions, Json);
    }
}

/// <summary>The board's current lists, labels and members, which an automation's ids must refer to.</summary>
public sealed record BoardContext(
    IReadOnlyDictionary<Guid, string> Lists,
    IReadOnlyDictionary<Guid, string> Labels,
    IReadOnlyDictionary<Guid, (string Name, BoardRole Role)> Members);

/// <summary>Trigger, condition and action types, and validation of an automation against its board.</summary>
public static class AutomationCatalog
{
    public const string CardEntered = "card.entered";
    public const string CardLeft = "card.left";
    public const string CardCreated = "card.created";
    public const string CardCompleted = "card.completed";
    public const string CardReopened = "card.reopened";
    public const string LabelAdded = "label.added";
    public const string LabelRemoved = "label.removed";
    public const string AssigneesChanged = "assignees.changed";
    public const string ChecklistDone = "checklist.done";
    public const string CommentAdded = "comment.added";

    public const string DueSoon = "due.soon";
    public const string DueOverdue = "due.overdue";
    public const string ListStale = "list.stale";
    public const string CompletedAged = "completed.aged";

    public const string IfLabelAny = "label.any";
    public const string IfLabelNone = "label.none";
    public const string IfPriorityAtLeast = "priority.atLeast";
    public const string IfAssigneeSet = "assignee.set";
    public const string IfAssigneeNone = "assignee.none";
    public const string IfAssigneeIs = "assignee.is";
    public const string IfDueSet = "due.set";
    public const string IfChecklistDone = "checklist.done";

    public const string DoMove = "move";
    public const string DoComplete = "complete";
    public const string DoReopen = "reopen";
    public const string DoAddLabel = "label.add";
    public const string DoRemoveLabel = "label.remove";
    public const string DoAssign = "assign";
    public const string DoUnassign = "unassign";
    public const string DoPriority = "priority";
    public const string DoDue = "due";
    public const string DoChecklist = "checklist.add";
    public const string DoComment = "comment";
    public const string DoNotify = "notify";
    public const string DoArchive = "archive";

    /// <summary>✨ AI actions: run once the change is saved, charged to the rule creator's AI quota.</summary>
    public const string DoAiBreakdown = "ai.breakdown";
    public const string DoAiTriage = "ai.triage";
    public const string DoAiSummary = "ai.summary";

    public const int MaxConditions = 5;
    public const int MaxChecklistItems = 10;
    public const int MaxCommentLength = 1_000;
    public const int MaxNotifyLength = 200;

    public static readonly IReadOnlySet<string> InstantTriggers = new HashSet<string>(StringComparer.Ordinal)
    {
        CardEntered, CardLeft, CardCreated, CardCompleted, CardReopened, LabelAdded, LabelRemoved,
        AssigneesChanged, ChecklistDone, CommentAdded,
    };

    /// <summary>Checked by the hourly <c>automation-timers</c> job, so they may run late while the app sleeps.</summary>
    public static readonly IReadOnlySet<string> HourlyTriggers = new HashSet<string>(StringComparer.Ordinal)
    {
        DueSoon, DueOverdue, ListStale, CompletedAged,
    };

    private static readonly HashSet<string> ConditionTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        IfLabelAny, IfLabelNone, IfPriorityAtLeast, IfAssigneeSet, IfAssigneeNone, IfAssigneeIs, IfDueSet, IfChecklistDone,
    };

    /// <summary>Requirements a fix-mode doorway can't fill in by itself.</summary>
    private static readonly HashSet<string> Unfixable = new HashSet<string>(StringComparer.Ordinal) { IfDueSet, IfChecklistDone };

    private static readonly HashSet<string> ActionTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        DoMove, DoComplete, DoReopen, DoAddLabel, DoRemoveLabel, DoAssign, DoUnassign, DoPriority, DoDue,
        DoChecklist, DoComment, DoNotify, DoArchive, DoAiBreakdown, DoAiTriage, DoAiSummary,
    };

    public static bool IsHourly(string trigger) => HourlyTriggers.Contains(trigger);

    public static bool IsAi(string action) => action.StartsWith("ai.", StringComparison.Ordinal);

    /// <summary>
    /// Errors keyed by field (empty when valid). Every id must exist on <paramref name="board"/> now,
    /// so a rule that refers to a deleted label or archived list stops being valid.
    /// </summary>
    public static Dictionary<string, string[]> Validate(
        AutomationKind kind, Guid? listId, DoorwayDirection? direction, DoorwayMode? mode, AutomationSpec spec, BoardContext board)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(board);
        var errors = new Dictionary<string, List<string>>();
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        bool ListOk(Guid? id) => id is { } value && board.Lists.ContainsKey(value);
        bool LabelOk(Guid? id) => id is { } value && board.Labels.ContainsKey(value);
        bool MemberOk(Guid? id) => id is { } value && board.Members.ContainsKey(value);

        if (kind == AutomationKind.Doorway)
        {
            if (!ListOk(listId))
            {
                Add("listId", "The doorway's list is not an active list on this board.");
            }

            if (direction is null || mode is null)
            {
                Add("mode", "A doorway needs a direction and a mode.");
            }

            if (mode == DoorwayMode.Fix && spec.Conditions.Any(c => Unfixable.Contains(c.Type)))
            {
                Add("conditions", "A due date or a finished checklist can't be filled in automatically. Use block or warn.");
            }

            if (spec.Conditions.Count == 0 && spec.Actions.Count == 0)
            {
                Add("conditions", "A doorway needs a requirement or an action.");
            }
        }
        else
        {
            ValidateTrigger(spec.Trigger, Add, ListOk, LabelOk);
            if (spec.Actions.Count == 0)
            {
                Add("actions", "Add at least one action.");
            }
        }

        if (spec.Conditions.Count > MaxConditions)
        {
            Add("conditions", $"Use at most {MaxConditions} conditions.");
        }

        foreach (var condition in spec.Conditions)
        {
            if (!ConditionTypes.Contains(condition.Type))
            {
                Add("conditions", $"Unknown condition '{condition.Type}'.");
            }
            else if (condition.Type is IfLabelAny or IfLabelNone
                && (condition.LabelIds is not { Count: > 0 } ids || !ids.All(id => LabelOk(id))))
            {
                Add("conditions", "Pick labels that exist on this board.");
            }
            else if (condition.Type == IfPriorityAtLeast && condition.Priority is null or CardPriority.None)
            {
                Add("conditions", "Pick a priority.");
            }
            else if (condition.Type == IfAssigneeIs && !MemberOk(condition.UserId))
            {
                Add("conditions", "Pick a member of this board.");
            }
        }

        if (spec.Actions.Count > AutomationRule.MaxActions)
        {
            Add("actions", $"Use at most {AutomationRule.MaxActions} actions.");
        }

        foreach (var action in spec.Actions)
        {
            ValidateAction(action, Add, ListOk, LabelOk, MemberOk, board);
        }

        var ai = spec.Actions.Count(a => IsAi(a.Type));
        if (ai > 1)
        {
            Add("actions", "Use at most one ✨ AI action.");
        }

        if (ai > 0 && (kind == AutomationKind.Doorway || IsHourly(spec.Trigger.Type)))
        {
            Add("actions", "✨ AI actions only run on rules with an instant trigger.");
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
    }

    private static void ValidateTrigger(TriggerSpec trigger, Action<string, string> add, Func<Guid?, bool> listOk, Func<Guid?, bool> labelOk)
    {
        if (!InstantTriggers.Contains(trigger.Type) && !HourlyTriggers.Contains(trigger.Type))
        {
            add("trigger", $"Unknown trigger '{trigger.Type}'.");
            return;
        }

        switch (trigger.Type)
        {
            case CardEntered or CardLeft or ListStale when !listOk(trigger.ListId):
                add("trigger", "Pick an active list on this board.");
                break;
            case CardCreated when trigger.ListId is not null && !listOk(trigger.ListId):
                add("trigger", "Pick an active list on this board.");
                break;
            case LabelAdded or LabelRemoved when !labelOk(trigger.LabelId):
                add("trigger", "Pick a label that exists on this board.");
                break;
            case DueSoon when trigger.Days is not (>= 1 and <= 30):
                add("trigger", "Days before the due date must be 1 to 30.");
                break;
            case ListStale or CompletedAged when trigger.Days is not (>= 1 and <= 90):
                add("trigger", "Days must be 1 to 90.");
                break;
        }
    }

    private static void ValidateAction(
        ActionSpec action, Action<string, string> add, Func<Guid?, bool> listOk, Func<Guid?, bool> labelOk, Func<Guid?, bool> memberOk, BoardContext board)
    {
        switch (action.Type)
        {
            case DoMove when !listOk(action.ListId):
                add("actions", "Pick an active list to move to.");
                break;
            case DoMove when action.Position is not (null or "top" or "bottom"):
                add("actions", "Move to the top or the bottom.");
                break;
            case DoAddLabel or DoRemoveLabel when !labelOk(action.LabelId):
                add("actions", "Pick a label that exists on this board.");
                break;
            case DoAssign when action.Who is not ("actor" or "creator" or "member"):
                add("actions", "Assign the person who triggered it, the card's creator or a member.");
                break;
            case DoAssign when action.Who == "member"
                && (!memberOk(action.UserId) || board.Members[action.UserId!.Value].Role < BoardRole.Editor):
                add("actions", "Pick a member who can edit this board.");
                break;
            case DoPriority when action.Priority is null:
                add("actions", "Pick a priority.");
                break;
            case DoDue when action.Days is not (>= 0 and <= 365):
                add("actions", "The due date must be 0 to 365 days ahead.");
                break;
            case DoChecklist when action.Items is not { Count: >= 1 and <= MaxChecklistItems } items
                || items.Any(i => string.IsNullOrWhiteSpace(i) || i.Length > ChecklistItem.TextMaxLength):
                add("actions", $"Add 1 to {MaxChecklistItems} checklist items of up to {ChecklistItem.TextMaxLength} characters.");
                break;
            case DoComment when string.IsNullOrWhiteSpace(action.Text) || action.Text.Length > MaxCommentLength:
                add("actions", $"Write a comment of up to {MaxCommentLength} characters.");
                break;
            case DoNotify when action.Who is not ("assignees" or "owners" or "members"):
                add("actions", "Notify the assignees, the owners or all members.");
                break;
            case DoNotify when action.Text is { Length: > MaxNotifyLength }:
                add("actions", $"Keep the note under {MaxNotifyLength} characters.");
                break;
            default:
                if (!ActionTypes.Contains(action.Type))
                {
                    add("actions", $"Unknown action '{action.Type}'.");
                }

                break;
        }
    }
}
