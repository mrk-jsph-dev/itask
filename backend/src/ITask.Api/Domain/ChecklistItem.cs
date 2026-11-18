using ITask.Api.Common.Ordering;

namespace ITask.Api.Domain;

/// <summary>A sub-task on a card, ordered by <see cref="Position"/>.</summary>
public class ChecklistItem
{
    public const int TextMaxLength = 300;

    /// <summary>Most items one card can hold.</summary>
    public const int MaxPerCard = 100;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CardId { get; set; }

    public Card? Card { get; set; }

    public required string Text { get; set; }

    public bool IsDone { get; set; }

    /// <summary>Sort key within the card; see <see cref="PositionCalculator"/>.</summary>
    public double Position { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public Guid? AssigneeId { get; set; }

    public AppUser? Assignee { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
