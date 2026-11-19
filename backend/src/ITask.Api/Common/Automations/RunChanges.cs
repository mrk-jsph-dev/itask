using ITask.Api.Domain;

namespace ITask.Api.Common.Automations;

public sealed record ListChange(Guid FromId, double FromPosition, Guid ToId, double ToPosition);

public sealed record DateChange(DateTimeOffset? Before, DateTimeOffset? After);

public sealed record PriorityChange(CardPriority Before, CardPriority After);

/// <summary>What one automation run changed on its card; stored as <see cref="AutomationRun.UndoJson"/>.</summary>
public sealed class RunChanges
{
    public ListChange? List { get; set; }

    public DateChange? CompletedAt { get; set; }

    public DateChange? DueAt { get; set; }

    public DateChange? ArchivedAt { get; set; }

    public PriorityChange? Priority { get; set; }

    public List<Guid> LabelsAdded { get; init; } = [];

    public List<Guid> LabelsRemoved { get; init; } = [];

    public List<Guid> AssigneesAdded { get; init; } = [];

    public List<Guid> AssigneesRemoved { get; init; } = [];

    public List<Guid> ChecklistItems { get; init; } = [];

    public Guid? CommentId { get; set; }

    public bool IsEmpty =>
        List is null && CompletedAt is null && DueAt is null && ArchivedAt is null && Priority is null
        && LabelsAdded.Count + LabelsRemoved.Count + AssigneesAdded.Count + AssigneesRemoved.Count + ChecklistItems.Count == 0
        && CommentId is null;
}
