using System.Text;
using ITask.Api.Common.Ai;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Ai.BoardDigest;

/// <summary>
/// Short stand-up summary of one board: open cards due within two days or overdue, open cards with no
/// activity for a week (outside the last list), and cards completed in the last day. Editor or above.
/// </summary>
public static class BoardDigest
{
    public const int MaxDigestLength = 2_000;

    private const int MaxCardsPerPart = 30;

    public sealed record Response(string Digest, int Due, int Stuck, int Done, int Remaining);

    private sealed record Row(
        Guid Id,
        int Number,
        string Title,
        Guid ListId,
        string List,
        CardPriority Priority,
        DateTimeOffset? DueAt,
        DateTimeOffset? CompletedAt,
        DateTimeOffset CreatedAt,
        List<string> Assignees);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/ai/digest", Handle)
                .RequireAi();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId, AppDbContext db, IBoardAccess access, ICurrentUser user, AiGateway gateway, TimeProvider time, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var now = time.GetUtcNow();
            var zone = Recurrences.Zone(await db.Users.Where(u => u.Id == user.Id).Select(u => u.TimeZone).FirstAsync(ct));
            var board = await db.Boards.AsNoTracking().Where(b => b.Id == boardId)
                .Select(b => new { b.Name, b.KeyPrefix }).FirstAsync(ct);
            var lastListId = await db.Lists.AsNoTracking()
                .Where(l => l.BoardId == boardId && l.ArchivedAt == null)
                .OrderByDescending(l => l.Position).Select(l => (Guid?)l.Id).FirstOrDefaultAsync(ct);

            var cards = await db.Cards.AsNoTracking()
                .Where(c => c.BoardId == boardId && c.ArchivedAt == null && c.List!.ArchivedAt == null)
                .Select(c => new Row(
                    c.Id,
                    c.Number,
                    c.Title,
                    c.ListId,
                    c.List!.Name,
                    c.Priority,
                    c.DueAt,
                    c.CompletedAt,
                    c.CreatedAt,
                    c.Assignees.Select(a => a.User!.DisplayName).ToList()))
                .ToListAsync(ct);

            var weekAgo = now.AddDays(-7);
            var recentlyActive = (await db.Activity.AsNoTracking()
                    .Where(a => a.BoardId == boardId && a.CardId != null && a.CreatedAt >= weekAgo)
                    .Select(a => a.CardId!.Value)
                    .Distinct()
                    .ToListAsync(ct))
                .ToHashSet();

            var dueLimit = now.AddDays(2);
            var due = cards.Where(c => c.CompletedAt == null && c.DueAt is { } d && d <= dueLimit)
                .OrderBy(c => c.DueAt).ToList();
            var stuck = cards.Where(c => c.CompletedAt == null && c.ListId != lastListId
                    && c.CreatedAt < weekAgo && !recentlyActive.Contains(c.Id) && !due.Contains(c))
                .OrderByDescending(c => c.Priority).ThenBy(c => c.CreatedAt).ToList();
            var done = cards.Where(c => c.CompletedAt is { } d && d >= now.AddDays(-1))
                .OrderByDescending(c => c.CompletedAt).ToList();

            var prompt = new StringBuilder()
                .Append("Board: ").Append(board.Name).Append('\n')
                .Append("Now: ").Append(AiText.Day(now, zone)).Append("\n\n");
            void Part(string title, List<Row> rows)
            {
                prompt.Append(title).Append(" (").Append(rows.Count).Append("):\n");
                foreach (var c in rows.Take(MaxCardsPerPart))
                {
                    prompt.Append("- ").Append(Card.Key(board.KeyPrefix, c.Number)).Append(' ').Append(AiText.Cut(c.Title, 120))
                        .Append(" | list ").Append(c.List)
                        .Append(" | priority ").Append(c.Priority)
                        .Append(" | due ").Append(c.DueAt is { } d ? AiText.Day(d, zone) : "none")
                        .Append(" | ").Append(c.Assignees.Count > 0 ? string.Join(", ", c.Assignees) : "unassigned").Append('\n');
                }

                prompt.Append('\n');
            }

            Part("Due or overdue", due);
            Part("Stuck (no activity for 7 days)", stuck);
            Part("Done in the last day", done);

            var outcome = await gateway.RunAsync(
                AiPrompts.Digest, AiText.Cut(prompt.ToString(), AiText.MaxPromptChars), 400, text => AiDrafts.ParseText(text, MaxDigestLength), ct);
            return outcome.Problem is { } problem
                ? problem
                : TypedResults.Ok(new Response(outcome.Value!, due.Count, stuck.Count, done.Count, outcome.Remaining));
        }
    }
}
