using System.Linq.Expressions;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Cards;

public sealed record LabelChip(Guid Id, string Name, string Color, string? Emoji);

public sealed record AssigneeChip(Guid UserId, string DisplayName, string AvatarColor);

/// <summary>
/// What a card tile shows. <c>RowVersion</c> (base64) goes back with MoveCard.
/// </summary>
public sealed record CardSummary(
    Guid Id,
    Guid ListId,
    int Number,
    string Key,
    string Title,
    double Position,
    DateTimeOffset? DueAt,
    DateTimeOffset? StartAt,
    CardPriority Priority,
    string? CoverColor,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ArchivedAt,
    IReadOnlyList<LabelChip> Labels,
    IReadOnlyList<AssigneeChip> Assignees,
    int ChecklistDone,
    int ChecklistTotal,
    int CommentCount,
    string RowVersion);

/// <summary>Loads <see cref="CardSummary"/> rows in one query per call.</summary>
public static class CardSummaries
{
    /// <summary>Summaries of the board's cards matching <paramref name="filter"/>, ordered by list position.</summary>
    public static async Task<List<CardSummary>> QueryAsync(
        AppDbContext db, Guid boardId, Expression<Func<Card, bool>> filter, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var prefix = await db.Boards.Where(b => b.Id == boardId).Select(b => b.KeyPrefix).FirstAsync(ct);
        var rows = await db.Cards
            .AsNoTracking()
            .Where(c => c.BoardId == boardId)
            .Where(filter)
            .OrderBy(c => c.Position)
            .Select(c => new
            {
                c.Id,
                c.ListId,
                c.Number,
                c.Title,
                c.Position,
                c.DueAt,
                c.StartAt,
                c.Priority,
                c.CoverColor,
                c.CompletedAt,
                c.ArchivedAt,
                Labels = c.Labels
                    .Select(l => new LabelChip(l.Label!.Id, l.Label.Name, l.Label.Color, l.Label.Emoji))
                    .ToList(),
                Assignees = c.Assignees
                    .Select(a => new AssigneeChip(a.UserId, a.User!.DisplayName, a.User.AvatarColor))
                    .ToList(),
                ChecklistDone = c.Checklist.Count(i => i.IsDone),
                ChecklistTotal = c.Checklist.Count,
                CommentCount = c.Comments.Count,
                c.RowVersion,
            })
            .ToListAsync(ct);

        return [.. rows.Select(c => new CardSummary(
            c.Id,
            c.ListId,
            c.Number,
            Card.Key(prefix, c.Number),
            c.Title,
            c.Position,
            c.DueAt,
            c.StartAt,
            c.Priority,
            c.CoverColor,
            c.CompletedAt,
            c.ArchivedAt,
            [.. c.Labels.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)],
            [.. c.Assignees.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)],
            c.ChecklistDone,
            c.ChecklistTotal,
            c.CommentCount,
            Convert.ToBase64String(c.RowVersion)))];
    }

    /// <summary>One card's summary, or null when it isn't on the board.</summary>
    public static async Task<CardSummary?> FindAsync(AppDbContext db, Guid boardId, Guid cardId, CancellationToken ct) =>
        (await QueryAsync(db, boardId, c => c.Id == cardId, ct)).FirstOrDefault();
}
