namespace ITask.Api.Domain;

/// <summary>
/// Something a user should hear about. <see cref="Data"/> is JSON with the names involved at the
/// time, so the notification still reads correctly after renames and archives.
/// </summary>
public class Notification
{
    public const int TypeMaxLength = 32;

    public const int KeyMaxLength = 100;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public AppUser? User { get; set; }

    /// <summary>See <c>NotificationTypes</c>.</summary>
    public required string Type { get; set; }

    public Guid BoardId { get; set; }

    public Board? Board { get; set; }

    /// <summary>The card it is about, if any. Not a foreign key: cards are archived, not deleted.</summary>
    public Guid? CardId { get; set; }

    /// <summary>Who caused it; null for scheduled reminders.</summary>
    public Guid? ActorId { get; set; }

    public AppUser? Actor { get; set; }

    public string Data { get; set; } = "{}";

    /// <summary>Unique per user when set, so a scheduled reminder is created once.</summary>
    public string? Key { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
