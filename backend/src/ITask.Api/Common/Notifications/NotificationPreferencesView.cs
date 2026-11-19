using ITask.Api.Domain;

namespace ITask.Api.Common.Notifications;

/// <summary>Which notification types the digest includes.</summary>
public sealed record DigestTypes(bool Mentioned, bool Assigned, bool DueSoon, bool Overdue, bool Invited, bool CommentOnMyCard);

/// <summary>
/// Email digest settings. <c>EmailConfirmed</c> tells the client whether digests can reach the user
/// (only confirmed addresses get them).
/// </summary>
public sealed record NotificationPreferencesView(DigestFrequency Digest, DigestTypes Types, bool EmailConfirmed)
{
    public static NotificationPreferencesView From(NotificationPreference p, bool emailConfirmed)
    {
        ArgumentNullException.ThrowIfNull(p);
        return new NotificationPreferencesView(
            p.Digest,
            new DigestTypes(p.Mentioned, p.Assigned, p.DueSoon, p.Overdue, p.Invited, p.CommentOnMyCard),
            emailConfirmed);
    }
}
