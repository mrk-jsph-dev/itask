namespace ITask.Api.Domain;

/// <summary>
/// <see cref="CardId"/> waits on <see cref="BlockedById"/>. Both cards are on the same board. A card is
/// blocked while at least one of its blockers is open (not completed and not archived).
/// </summary>
public class CardDependency
{
    /// <summary>Most blockers one card can have.</summary>
    public const int MaxBlockersPerCard = 10;

    /// <summary>The blocked card.</summary>
    public Guid CardId { get; set; }

    public Card? Card { get; set; }

    /// <summary>The card that has to be finished first.</summary>
    public Guid BlockedById { get; set; }

    public Card? BlockedBy { get; set; }

    public Guid CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
