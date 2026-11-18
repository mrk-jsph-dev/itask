using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Activity;

/// <summary>
/// Adds activity entries to the current <see cref="AppDbContext"/> without saving, so they are
/// written by the slice's own <c>SaveChangesAsync</c>, in the same transaction as the change.
/// </summary>
public interface IActivityRecorder
{
    /// <param name="data">Serialized camelCase; nulls are left out.</param>
    void Record(string type, Guid boardId, Guid? cardId, object? data = null);

    /// <summary>
    /// Records a card change and adds <c>card: { key, title }</c> to <paramref name="data"/>, using the
    /// tracked card when there is one so a title changed in the same request is the one shown.
    /// </summary>
    Task RecordCardAsync(string type, Guid boardId, Guid cardId, object? data, CancellationToken ct);
}

internal sealed class ActivityRecorder(AppDbContext db, ICurrentUser user, TimeProvider clock) : IActivityRecorder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void Record(string type, Guid boardId, Guid? cardId, object? data = null) =>
        Add(type, boardId, cardId, data is null ? "{}" : JsonSerializer.Serialize(data, Json));

    public async Task RecordCardAsync(string type, Guid boardId, Guid cardId, object? data, CancellationToken ct)
    {
        var card = db.Cards.Local.FirstOrDefault(c => c.Id == cardId) is { } tracked
            ? new { tracked.Number, tracked.Title }
            : await db.Cards.AsNoTracking().Where(c => c.Id == cardId).Select(c => new { c.Number, c.Title }).FirstAsync(ct);
        var prefix = db.Boards.Local.FirstOrDefault(b => b.Id == boardId)?.KeyPrefix
            ?? await db.Boards.AsNoTracking().Where(b => b.Id == boardId).Select(b => b.KeyPrefix).FirstAsync(ct);

        var node = data is null ? [] : JsonSerializer.SerializeToNode(data, Json)!.AsObject();
        node["card"] = new JsonObject
        {
            ["key"] = Card.Key(prefix, card.Number),
            ["title"] = card.Title,
        };
        Add(type, boardId, cardId, node.ToJsonString(Json));
    }

    private void Add(string type, Guid boardId, Guid? cardId, string data) =>
        db.Activity.Add(new ActivityEntry
        {
            Type = type,
            BoardId = boardId,
            CardId = cardId,
            ActorId = user.Id,
            Data = data,
            CreatedAt = clock.GetUtcNow(),
        });
}
