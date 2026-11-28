using System.Text.Json.Serialization;

namespace ITask.Api.Domain;

[JsonConverter(typeof(JsonStringEnumConverter<DigestFrequency>))]
public enum DigestFrequency
{
    Off,
    Daily,
    Weekly,
}

/// <summary>
/// A user's email digest settings. In-app notifications are always on; the per-type flags only
/// choose what the digest includes. No row means the defaults.
/// </summary>
public class NotificationPreference
{
    public Guid UserId { get; set; }

    public DigestFrequency Digest { get; set; } = DigestFrequency.Off;

    public bool Mentioned { get; set; } = true;

    public bool Assigned { get; set; } = true;

    public bool DueSoon { get; set; } = true;

    public bool Overdue { get; set; } = true;

    public bool Invited { get; set; } = true;

    public bool CommentOnMyCard { get; set; } = true;

    public bool Unblocked { get; set; } = true;

    /// <summary>When the last digest went out (or was skipped as empty).</summary>
    public DateTimeOffset? LastDigestAt { get; set; }
}
