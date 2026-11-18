namespace ITask.Api.Domain;

/// <summary>A user's favourite mark on a board. Stars are per user, not per board.</summary>
public class BoardStar
{
    public Guid BoardId { get; set; }

    public Board? Board { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
