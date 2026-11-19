using ITask.Api.Domain;

namespace ITask.Api.Common.Notifications;

/// <summary>
/// Notification types. The frontend turns each type and its data into a sentence
/// (<c>features/notifications/format.ts</c>).
/// </summary>
public static class NotificationTypes
{
    public const string Mentioned = "mentioned";
    public const string Assigned = "assigned";
    public const string DueSoon = "due_soon";
    public const string Overdue = "overdue";
    public const string Invited = "invited";
    public const string CommentOnMyCard = "comment_on_my_card";

    /// <summary>Sent by a board automation's notify action; in-app only.</summary>
    public const string Automation = "automation";

    /// <summary>Whether <paramref name="type"/> goes into the email digest under <paramref name="prefs"/>.</summary>
    public static bool InDigest(string type, NotificationPreference prefs)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        return type switch
        {
            Mentioned => prefs.Mentioned,
            Assigned => prefs.Assigned,
            DueSoon => prefs.DueSoon,
            Overdue => prefs.Overdue,
            Invited => prefs.Invited,
            CommentOnMyCard => prefs.CommentOnMyCard,
            _ => false,
        };
    }
}
