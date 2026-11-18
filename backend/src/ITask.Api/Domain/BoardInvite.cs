namespace ITask.Api.Domain;

/// <summary>
/// A single-use invitation to join a board. Only the SHA-256 hash of the token is stored;
/// the raw token is returned once when the invite is created.
/// </summary>
public class BoardInvite
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BoardId { get; set; }

    public Board? Board { get; set; }

    /// <summary>When set, only the account with this email can accept. Null for link invites.</summary>
    public string? Email { get; set; }

    public BoardRole Role { get; set; }

    public required string TokenHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }

    public Guid CreatedById { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }
}
