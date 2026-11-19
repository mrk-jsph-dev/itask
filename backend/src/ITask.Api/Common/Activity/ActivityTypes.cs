namespace ITask.Api.Common.Activity;

/// <summary>
/// Activity entry types. The part before the dot is the category the feeds filter on.
/// The frontend turns each type and its data into a sentence (<c>features/activity/format.ts</c>).
/// </summary>
public static class ActivityTypes
{
    public const string BoardCreated = "board.created";
    public const string BoardUpdated = "board.updated";
    public const string BoardArchived = "board.archived";
    public const string BoardRestored = "board.restored";

    public const string MemberJoined = "member.joined";
    public const string MemberRemoved = "member.removed";
    public const string MemberLeft = "member.left";
    public const string MemberRoleChanged = "member.role_changed";

    public const string ListCreated = "list.created";
    public const string ListRenamed = "list.renamed";
    public const string ListUpdated = "list.updated";
    public const string ListMoved = "list.moved";
    public const string ListArchived = "list.archived";
    public const string ListRestored = "list.restored";

    public const string CardCreated = "card.created";
    public const string CardUpdated = "card.updated";
    public const string CardMoved = "card.moved";
    public const string CardArchived = "card.archived";
    public const string CardRestored = "card.restored";
    public const string CardCompleted = "card.completed";
    public const string CardReopened = "card.reopened";
    public const string CardAssigneesChanged = "card.assignees_changed";
    public const string CardLabelsChanged = "card.labels_changed";
    public const string CardRecurrenceSet = "card.recurrence_set";
    public const string CardRecurrenceCleared = "card.recurrence_cleared";

    public const string LabelCreated = "label.created";
    public const string LabelUpdated = "label.updated";
    public const string LabelDeleted = "label.deleted";

    public const string ChecklistAdded = "checklist.added";
    public const string ChecklistUpdated = "checklist.updated";
    public const string ChecklistChecked = "checklist.checked";
    public const string ChecklistUnchecked = "checklist.unchecked";
    public const string ChecklistMoved = "checklist.moved";
    public const string ChecklistDeleted = "checklist.deleted";

    public const string CommentAdded = "comment.added";
    public const string CommentEdited = "comment.edited";
    public const string CommentDeleted = "comment.deleted";

    public const string AttachmentAdded = "attachment.added";
    public const string AttachmentDeleted = "attachment.deleted";

    public const string AutomationCreated = "automation.created";
    public const string AutomationUpdated = "automation.updated";
    public const string AutomationDeleted = "automation.deleted";
    public const string AutomationDisabled = "automation.disabled";
    public const string AutomationUndone = "automation.undone";
    public const string AutomationAiSkipped = "automation.ai_skipped";
    public const string DoorwayOverridden = "doorway.overridden";

    /// <summary>Categories accepted by the feeds' <c>type</c> filter.</summary>
    public static readonly IReadOnlySet<string> Categories = new HashSet<string>(StringComparer.Ordinal)
    {
        "board", "member", "list", "card", "label", "checklist", "comment", "attachment", "automation", "doorway",
    };
}
