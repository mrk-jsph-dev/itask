namespace ITask.Api.Domain;

/// <summary>
/// One change on a board. <see cref="Data"/> is JSON with the names involved at the time of the
/// change, so the entry still reads correctly after renames, archives and deletes.
/// </summary>
public class ActivityEntry
{
    public const int TypeMaxLength = 40;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BoardId { get; set; }

    public Board? Board { get; set; }

    /// <summary>The card the change belongs to, if any. Not a foreign key: cards are archived, not deleted.</summary>
    public Guid? CardId { get; set; }

    public Guid ActorId { get; set; }

    public AppUser? Actor { get; set; }

    /// <summary>Dotted type such as <c>card.moved</c>; see <c>ActivityTypes</c>.</summary>
    public required string Type { get; set; }

    public string Data { get; set; } = "{}";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
