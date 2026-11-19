using System.Linq.Expressions;
using System.Text.Json;
using ITask.Api.Common.Paging;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Notifications;

public sealed record NotificationBoardRef(Guid Id, string Name, string? Emoji);

public sealed record NotificationActor(Guid Id, string DisplayName, string AvatarColor);

/// <summary>One notification. <c>Data</c> is the JSON recorded with it; <c>Actor</c> is null for reminders.</summary>
public sealed record NotificationView(
    Guid Id,
    string Type,
    JsonElement Data,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    Guid? CardId,
    NotificationBoardRef Board,
    NotificationActor? Actor);

public sealed record NotificationPage(IReadOnlyList<NotificationView> Items, string? NextCursor);

/// <summary>Keyset paging over a user's notifications, newest first.</summary>
public static class NotificationFeed
{
    private sealed record Row(
        Guid Id, string Type, string Data, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, Guid? CardId,
        Guid BoardId, string BoardName, string? BoardEmoji, Guid? ActorId, string? ActorName, string? ActorColor);

    private static readonly Expression<Func<Notification, Row>> Project = n => new Row(
        n.Id, n.Type, n.Data, n.CreatedAt, n.ReadAt, n.CardId,
        n.BoardId, n.Board!.Name, n.Board.Emoji, n.ActorId, n.Actor!.DisplayName, n.Actor.AvatarColor);

    /// <summary>Validate <paramref name="cursor"/> with <see cref="TimeCursor.TryParse"/> first: an invalid one is ignored here.</summary>
    public static async Task<NotificationPage> PageAsync(
        IQueryable<Notification> query, string? cursor, int? limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (cursor is not null && TimeCursor.TryParse(cursor, out var createdAt, out var id))
        {
            query = query.Where(n => n.CreatedAt < createdAt || (n.CreatedAt == createdAt && n.Id.CompareTo(id) < 0));
        }

        var take = TimeCursor.Take(limit);
        var rows = await query
            .AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Take(take + 1)
            .Select(Project)
            .ToListAsync(ct);

        var next = rows.Count > take ? TimeCursor.Format(rows[take - 1].CreatedAt, rows[take - 1].Id) : null;
        return new NotificationPage([.. rows.Take(take).Select(ToView)], next);
    }

    private static NotificationView ToView(Row r)
    {
        using var data = JsonDocument.Parse(r.Data);
        return new NotificationView(
            r.Id,
            r.Type,
            data.RootElement.Clone(),
            r.CreatedAt,
            r.ReadAt,
            r.CardId,
            new NotificationBoardRef(r.BoardId, r.BoardName, r.BoardEmoji),
            r.ActorId is { } actorId ? new NotificationActor(actorId, r.ActorName!, r.ActorColor!) : null);
    }
}
