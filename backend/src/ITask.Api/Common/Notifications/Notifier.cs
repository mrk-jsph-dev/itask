using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Notifications;

/// <summary>
/// Adds notifications to the current <see cref="AppDbContext"/> without saving, so they are
/// written by the slice's own <c>SaveChangesAsync</c>, in the same transaction as the change.
/// The signed-in user is never notified about their own action.
/// </summary>
public interface INotifier
{
    /// <param name="data">Serialized camelCase; nulls are left out.</param>
    void Notify(string type, IEnumerable<Guid> userIds, Guid boardId, object? data = null);

    /// <summary>Notifies about a card and adds <c>card: { key, title }</c> to <paramref name="data"/>.</summary>
    Task NotifyCardAsync(string type, IEnumerable<Guid> userIds, Guid boardId, Guid cardId, object? data, CancellationToken ct);
}

internal sealed class Notifier(AppDbContext db, ICurrentUser user, TimeProvider clock) : INotifier
{
    public void Notify(string type, IEnumerable<Guid> userIds, Guid boardId, object? data = null) =>
        Add(type, userIds, boardId, null, NotificationData.Serialize(data));

    public async Task NotifyCardAsync(
        string type, IEnumerable<Guid> userIds, Guid boardId, Guid cardId, object? data, CancellationToken ct)
    {
        var recipients = Recipients(userIds);
        if (recipients.Count == 0)
        {
            return;
        }

        var card = db.Cards.Local.FirstOrDefault(c => c.Id == cardId) is { } tracked
            ? new { tracked.Number, tracked.Title }
            : await db.Cards.AsNoTracking().Where(c => c.Id == cardId).Select(c => new { c.Number, c.Title }).FirstAsync(ct);
        var prefix = db.Boards.Local.FirstOrDefault(b => b.Id == boardId)?.KeyPrefix
            ?? await db.Boards.AsNoTracking().Where(b => b.Id == boardId).Select(b => b.KeyPrefix).FirstAsync(ct);

        Add(type, recipients, boardId, cardId, NotificationData.WithCard(data, Card.Key(prefix, card.Number), card.Title));
    }

    private List<Guid> Recipients(IEnumerable<Guid> userIds)
    {
        var actor = user.Id;
        return [.. userIds.Where(id => id != actor).Distinct()];
    }

    private void Add(string type, IEnumerable<Guid> userIds, Guid boardId, Guid? cardId, string data)
    {
        var now = clock.GetUtcNow();
        foreach (var userId in Recipients(userIds))
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = type,
                BoardId = boardId,
                CardId = cardId,
                ActorId = user.Id,
                Data = data,
                CreatedAt = now,
            });
        }
    }
}

/// <summary>JSON payloads for <see cref="Notification.Data"/>.</summary>
public static class NotificationData
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(object? data) => data is null ? "{}" : JsonSerializer.Serialize(data, Json);

    public static string WithCard(object? data, string key, string title)
    {
        var node = data is null ? [] : JsonSerializer.SerializeToNode(data, Json)!.AsObject();
        node["card"] = new JsonObject { ["key"] = key, ["title"] = title };
        return node.ToJsonString(Json);
    }
}
