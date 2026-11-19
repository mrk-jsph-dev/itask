using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Automations;

/// <summary>
/// A card's labels, assignees and checklist as the current save will leave them: the stored rows
/// with the context's pending adds and removes applied.
/// </summary>
public sealed class CardState
{
    public required Card Card { get; init; }

    public required HashSet<Guid> Labels { get; init; }

    public required HashSet<Guid> Assignees { get; init; }

    public int ChecklistTotal { get; init; }

    public int ChecklistDone { get; init; }

    public static async Task<CardState> LoadAsync(AppDbContext db, Card card, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(card);

        var labels = (await db.CardLabels.AsNoTracking().Where(l => l.CardId == card.Id).Select(l => l.LabelId).ToListAsync(ct)).ToHashSet();
        foreach (var entry in db.ChangeTracker.Entries<CardLabel>().Where(e => e.Entity.CardId == card.Id))
        {
            Apply(labels, entry.Entity.LabelId, entry.State);
        }

        var assignees = (await db.CardAssignees.AsNoTracking().Where(a => a.CardId == card.Id).Select(a => a.UserId).ToListAsync(ct)).ToHashSet();
        foreach (var entry in db.ChangeTracker.Entries<CardAssignee>().Where(e => e.Entity.CardId == card.Id))
        {
            Apply(assignees, entry.Entity.UserId, entry.State);
        }

        // A tracking query returns the tracked instances, so pending ticks are already reflected.
        var items = await db.ChecklistItems.Where(i => i.CardId == card.Id).ToListAsync(ct);
        var pending = db.ChangeTracker.Entries<ChecklistItem>().Where(e => e.Entity.CardId == card.Id).ToList();
        var deleted = pending.Where(e => e.State == EntityState.Deleted).Select(e => e.Entity.Id).ToHashSet();
        var live = items.Where(i => !deleted.Contains(i.Id))
            .Concat(pending.Where(e => e.State == EntityState.Added).Select(e => e.Entity))
            .DistinctBy(i => i.Id)
            .ToList();

        return new CardState
        {
            Card = card,
            Labels = labels,
            Assignees = assignees,
            ChecklistTotal = live.Count,
            ChecklistDone = live.Count(i => i.IsDone),
        };
    }

    /// <summary>The state of a card that is being created: nothing on it yet.</summary>
    public static CardState Empty(Card card) => new() { Card = card, Labels = [], Assignees = [] };

    public bool ChecklistComplete => ChecklistTotal > 0 && ChecklistDone == ChecklistTotal;

    public bool Meets(ConditionSpec condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return condition.Type switch
        {
            AutomationCatalog.IfLabelAny => condition.LabelIds?.Any(Labels.Contains) == true,
            AutomationCatalog.IfLabelNone => condition.LabelIds?.Any(Labels.Contains) != true,
            AutomationCatalog.IfPriorityAtLeast => Card.Priority >= (condition.Priority ?? CardPriority.None),
            AutomationCatalog.IfAssigneeSet => Assignees.Count > 0,
            AutomationCatalog.IfAssigneeNone => Assignees.Count == 0,
            AutomationCatalog.IfAssigneeIs => condition.UserId is { } id && Assignees.Contains(id),
            AutomationCatalog.IfDueSet => Card.DueAt is not null,
            AutomationCatalog.IfChecklistDone => ChecklistComplete,
            _ => false,
        };
    }

    /// <summary>Plain-language description of a requirement, e.g. "an assignee".</summary>
    public static string Describe(ConditionSpec condition, BoardContext board)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(board);
        string Labels() => string.Join(" or ", (condition.LabelIds ?? []).Select(id => board.Labels.GetValueOrDefault(id, "a deleted label")));
        return condition.Type switch
        {
            AutomationCatalog.IfLabelAny => $"the label {Labels()}",
            AutomationCatalog.IfLabelNone => $"no label {Labels()}",
            AutomationCatalog.IfPriorityAtLeast => $"priority {condition.Priority} or higher",
            AutomationCatalog.IfAssigneeSet => "an assignee",
            AutomationCatalog.IfAssigneeNone => "nobody assigned",
            AutomationCatalog.IfAssigneeIs => $"{(condition.UserId is { } id && board.Members.TryGetValue(id, out var m) ? m.Name : "a former member")} assigned",
            AutomationCatalog.IfDueSet => "a due date",
            AutomationCatalog.IfChecklistDone => "a finished checklist",
            _ => condition.Type,
        };
    }

    private static void Apply(HashSet<Guid> set, Guid id, EntityState state)
    {
        if (state == EntityState.Added)
        {
            set.Add(id);
        }
        else if (state == EntityState.Deleted)
        {
            set.Remove(id);
        }
    }
}
