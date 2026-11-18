using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Profile.GetDueSoon;

/// <summary>
/// Open cards due within the next 7 days (overdue included) on the caller's active boards,
/// limited to cards assigned to the caller or to nobody. Counts cover every match; items are
/// the earliest <see cref="MaxItems"/>.
/// </summary>
public static class GetDueSoon
{
    public const int MaxItems = 8;

    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    public sealed record Item(
        Guid Id,
        Guid BoardId,
        string BoardName,
        string? BoardEmoji,
        string Title,
        string? Emoji,
        CardPriority Priority,
        DateTimeOffset DueAt);

    public sealed record Response(int DueThisWeek, int Overdue, IReadOnlyList<Item> Items);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/me/due-soon", Handle)
                .WithTags("Profile")
                .RequireAuthorization();

        private static async Task<Ok<Response>> Handle(
            ICurrentUser user, AppDbContext db, TimeProvider clock, CancellationToken ct)
        {
            var userId = user.Id;
            var now = clock.GetUtcNow();
            var until = now + Window;

            var rows = await db.Cards
                .AsNoTracking()
                .Where(c =>
                    c.DueAt != null
                    && c.CompletedAt == null
                    && c.ArchivedAt == null
                    && c.List!.ArchivedAt == null
                    && c.Board!.ArchivedAt == null
                    && db.BoardMembers.Any(m => m.BoardId == c.BoardId && m.UserId == userId)
                    && (!c.Assignees.Any() || c.Assignees.Any(a => a.UserId == userId)))
                .Select(c => new
                {
                    c.Id,
                    c.BoardId,
                    BoardName = c.Board!.Name,
                    BoardEmoji = c.Board.Emoji,
                    c.Title,
                    c.Emoji,
                    c.Priority,
                    DueAt = c.DueAt!.Value,
                })
                .ToListAsync(ct);

            // DateTimeOffset comparison and ordering run in memory: SQLite can't translate them.
            var due = rows.Where(r => r.DueAt <= until).OrderBy(r => r.DueAt).ThenBy(r => r.Title).ToList();
            var items = due
                .Take(MaxItems)
                .Select(r => new Item(r.Id, r.BoardId, r.BoardName, r.BoardEmoji, r.Title, r.Emoji, r.Priority, r.DueAt))
                .ToList();

            return TypedResults.Ok(new Response(due.Count, due.Count(r => r.DueAt < now), items));
        }
    }
}
