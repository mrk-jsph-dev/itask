using System.Globalization;
using System.Text.Json;
using ITask.Api.Common.Notifications;

namespace ITask.Api.Infrastructure.Notifications;

/// <summary>Digest sentences; keep in step with <c>describeNotification</c> in the frontend.</summary>
public static class DigestText
{
    public static string Describe(string type, string data, string? actor, string board, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        using var json = JsonDocument.Parse(data);
        var root = json.RootElement;
        var card = root.TryGetProperty("card", out var c)
            ? $"{c.GetProperty("key").GetString()} {c.GetProperty("title").GetString()}"
            : "a card";
        var who = actor ?? "Someone";

        return type switch
        {
            NotificationTypes.Mentioned => $"{who} mentioned you on {card} · {board}",
            NotificationTypes.Assigned => $"{who} assigned you to {card} · {board}",
            NotificationTypes.CommentOnMyCard => $"{who} commented on {card} · {board}",
            NotificationTypes.DueSoon => $"{card} is due {When(root, zone)} · {board}",
            NotificationTypes.Overdue => $"{card} is overdue · {board}",
            NotificationTypes.Invited => $"{who} invited you to {board}",
            NotificationTypes.Automation =>
                $"{(root.TryGetProperty("rule", out var rule) ? rule.GetString() : "An automation")} flagged {card} · {board}",
            _ => $"Update on {board}",
        };
    }

    private static string When(JsonElement root, TimeZoneInfo zone) =>
        root.TryGetProperty("dueAt", out var due) && due.TryGetDateTimeOffset(out var at)
            ? TimeZoneInfo.ConvertTime(at, zone).ToString("ddd d MMM, HH:mm", CultureInfo.InvariantCulture)
            : "soon";
}
