namespace ITask.Api.Domain;

/// <summary>
/// Repeat rule of a card. When the card is completed, or at <see cref="NextRunAt"/>, a copy is
/// created and the rule moves to that copy, so it always sits on the newest card of the series.
/// </summary>
public class CardRecurrence
{
    public const int TimeZoneMaxLength = 64;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CardId { get; set; }

    public Card? Card { get; set; }

    public RecurrenceKind Kind { get; set; }

    /// <summary>Bit <c>1 &lt;&lt; (int)DayOfWeek</c> per day, for <see cref="RecurrenceKind.Weekly"/>.</summary>
    public int Weekdays { get; set; }

    /// <summary>1 to 31, for <see cref="RecurrenceKind.Monthly"/>.</summary>
    public int? DayOfMonth { get; set; }

    /// <summary>List the copies go to. Not a foreign key; an archived or missing list falls back to the card's list.</summary>
    public Guid TargetListId { get; set; }

    /// <summary>IANA or Windows id; occurrences start at local midnight in this zone.</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Local midnight of the next occurrence, as UTC.</summary>
    public DateTimeOffset NextRunAt { get; set; }

    /// <summary>Who set the rule; the actor of copies the scheduler creates.</summary>
    public Guid CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
