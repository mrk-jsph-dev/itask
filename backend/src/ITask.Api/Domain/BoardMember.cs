namespace ITask.Api.Domain;

public class BoardMember
{
    public Guid BoardId { get; set; }

    public Board? Board { get; set; }

    public Guid UserId { get; set; }

    public AppUser? User { get; set; }

    public BoardRole Role { get; set; }

    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}
