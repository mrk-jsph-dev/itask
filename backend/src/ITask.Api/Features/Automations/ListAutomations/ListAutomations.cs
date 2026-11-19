using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Automations.ListAutomations;

/// <summary>
/// The board's automations, doorways first. Viewers see only the doorways (they are shown on the
/// columns); Editors and Owners see everything, with runs and blocked moves in the last 30 days.
/// </summary>
public static class ListAutomations
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/automations", Handle)
                .WithTags("Automations")
                .RequireAuthorization();

        private static async Task<Results<Ok<List<AutomationView>>, ProblemHttpResult>> Handle(
            Guid boardId, AppDbContext db, IBoardAccess access, TimeProvider clock, CancellationToken ct)
        {
            var role = await access.GetRoleAsync(boardId, ct);
            if (role is null)
            {
                return CardGuard.NotFound();
            }

            var query = db.AutomationRules.AsNoTracking().Where(r => r.BoardId == boardId);
            if (role < BoardRole.Editor)
            {
                query = query.Where(r => r.Kind == AutomationKind.Doorway);
            }

            var rules = (await query.ToListAsync(ct))
                .OrderBy(r => r.Kind == AutomationKind.Doorway ? 0 : 1)
                .ThenBy(r => r.CreatedAt)
                .ToList();
            var since = clock.GetUtcNow().AddDays(-AutomationView.RunWindowDays);
            var ids = rules.Select(r => r.Id).ToList();

            // SQLite can't compare DateTimeOffset in the query, so the window is applied in memory.
            var runs = (await db.AutomationRuns.AsNoTracking()
                    .Where(r => ids.Contains(r.RuleId) && r.Outcome != AutomationOutcome.Skipped)
                    .Select(r => new { r.RuleId, r.CreatedAt, r.Outcome })
                    .ToListAsync(ct))
                .Where(r => r.CreatedAt >= since)
                .GroupBy(r => r.RuleId)
                .ToDictionary(g => g.Key, g => (
                    Ran: g.Count(r => r.Outcome == AutomationOutcome.Ran),
                    Blocked: g.Count(r => r.Outcome == AutomationOutcome.Blocked),
                    Last: g.Max(r => r.CreatedAt)));

            var board = await AutomationBoards.LoadAsync(db, boardId, ct);
            return TypedResults.Ok(rules
                .Select(r => runs.TryGetValue(r.Id, out var run)
                    ? AutomationView.From(r, board, run.Ran, run.Blocked, run.Last)
                    : AutomationView.From(r, board))
                .ToList());
        }
    }
}
