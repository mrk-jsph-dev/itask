using System.Linq.Expressions;
using System.Text.Json;
using ITask.Api.Common.Paging;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Activity;

/// <summary>Filtering and keyset paging shared by the board, card and personal feeds.</summary>
public static class ActivityFeed
{
    private sealed record Row(
        Guid Id, string Type, string Data, DateTimeOffset CreatedAt, Guid? CardId,
        Guid BoardId, string BoardName, string? BoardEmoji, Guid ActorId, string ActorName, string ActorColor);

    private static readonly Expression<Func<ActivityEntry, Row>> Project = a => new Row(
        a.Id, a.Type, a.Data, a.CreatedAt, a.CardId,
        a.BoardId, a.Board!.Name, a.Board.Emoji, a.ActorId, a.Actor!.DisplayName, a.Actor.AvatarColor);

    /// <summary>400 for an unknown <paramref name="category"/> or a malformed cursor, otherwise null.</summary>
    public static ProblemHttpResult? Validate(string? category, string? cursor)
    {
        if (category is not null && !ActivityTypes.Categories.Contains(category))
        {
            return TypedResults.Problem(title: "Unknown activity type.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (cursor is not null && !TimeCursor.TryParse(cursor, out _, out _))
        {
            return TypedResults.Problem(title: "Invalid cursor.", statusCode: StatusCodes.Status400BadRequest);
        }

        return null;
    }

    /// <summary>Newest first. Call <see cref="Validate"/> first: an invalid cursor is ignored here.</summary>
    public static async Task<ActivityPage> PageAsync(
        IQueryable<ActivityEntry> query, string? category, string? cursor, int? limit, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (category is not null)
        {
            var prefix = category + ".";
            query = query.Where(a => a.Type.StartsWith(prefix));
        }

        if (cursor is not null && TimeCursor.TryParse(cursor, out var createdAt, out var id))
        {
            query = query.Where(a => a.CreatedAt < createdAt || (a.CreatedAt == createdAt && a.Id.CompareTo(id) < 0));
        }

        var take = TimeCursor.Take(limit);
        var rows = await query
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(take + 1)
            .Select(Project)
            .ToListAsync(ct);

        var next = rows.Count > take ? TimeCursor.Format(rows[take - 1].CreatedAt, rows[take - 1].Id) : null;
        return new ActivityPage([.. rows.Take(take).Select(ToView)], next);
    }

    private static ActivityView ToView(Row r)
    {
        using var data = JsonDocument.Parse(r.Data);
        return new ActivityView(
            r.Id,
            r.Type,
            data.RootElement.Clone(),
            r.CreatedAt,
            r.CardId,
            new ActivityBoardRef(r.BoardId, r.BoardName, r.BoardEmoji),
            new ActivityActor(r.ActorId, r.ActorName, r.ActorColor));
    }
}
