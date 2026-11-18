namespace ITask.Api.Domain;

/// <summary>A board member assigned to a card.</summary>
public class CardAssignee
{
    public Guid CardId { get; set; }

    public Card? Card { get; set; }

    public Guid UserId { get; set; }

    public AppUser? User { get; set; }
}
