using System.Text;
using ITask.Api.Common.Ai;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Ai.ReviewAutomations;

/// <summary>
/// "Review my board": suggests up to three automations from counts of what people did by hand in the
/// last 30 days. Only list, label and member names, the existing automations' names and aggregated
/// counts go to the model, never card text or comments. Editor or above; nothing is saved.
/// </summary>
public static class ReviewAutomations
{
    private const int Days = 30;

    private const int MaxEntries = 2_000;

    public sealed record Response(IReadOnlyList<AutomationSuggestion> Suggestions, int Remaining);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/ai/automation-review", Handle)
                .RequireAi();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId, AppDbContext db, IBoardAccess access, AiGateway gateway, TimeProvider clock, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var board = await AutomationBoards.LoadAsync(db, boardId, ct);
            var existing = await db.AutomationRules.AsNoTracking().Where(r => r.BoardId == boardId)
                .Select(r => new { r.Name, r.Kind }).ToListAsync(ct);
            var types = AutomationHistory.Types.ToList();
            var since = clock.GetUtcNow().AddDays(-Days);
            var entries = await db.Activity.AsNoTracking()
                .Where(a => a.BoardId == boardId && a.CardId != null && types.Contains(a.Type) && a.CreatedAt >= since)
                .OrderByDescending(a => a.Id)
                .Take(MaxEntries)
                .ToListAsync(ct);

            var counts = entries
                .SelectMany(e => AutomationHistory.Events(e, board))
                .GroupBy(e => Describe(e, board))
                .Select(g => (Text: g.Key, Count: g.Count()))
                .Where(c => c.Text is not null)
                .OrderByDescending(c => c.Count)
                .Take(30)
                .ToList();

            var prompt = new StringBuilder()
                .Append("Lists: ").AppendJoin(", ", board.Lists.Values).Append('\n')
                .Append("Labels: ").AppendJoin(", ", board.Labels.Values).Append('\n')
                .Append("Members: ").AppendJoin(", ", board.Members.Values.Select(m => m.Name)).Append('\n')
                .Append("Existing automations: ")
                .Append(existing.Count == 0 ? "none" : string.Join(", ", existing.Select(r => $"{r.Name} ({r.Kind})"))).Append("\n\n")
                .Append("Manual steps in the last 30 days:\n")
                .AppendJoin('\n', counts.Select(c => $"- {c.Text}: {c.Count}"))
                .ToString();

            var outcome = await gateway.RunAsync(
                AiPrompts.AutomationReview, prompt, 900, json => AutomationDrafts.ParseReview(json, board), ct, AutomationDrafts.ReviewSchema);
            return outcome.Problem is { } problem
                ? problem
                : TypedResults.Ok(new Response(outcome.Value!, outcome.Remaining));
        }

        private static string? Describe(HistoryEvent e, BoardContext board)
        {
            string List() => e.ListId is { } id && board.Lists.TryGetValue(id, out var name) ? name : "a list";
            string Label() => e.LabelId is { } id && board.Labels.TryGetValue(id, out var name) ? name : "a label";
            return e.Type switch
            {
                AutomationCatalog.CardEntered => $"cards moved into {List()}",
                AutomationCatalog.CardCreated => $"cards created in {List()}",
                AutomationCatalog.CardCompleted => "cards completed",
                AutomationCatalog.CardReopened => "cards reopened",
                AutomationCatalog.LabelAdded => $"label {Label()} added",
                AutomationCatalog.LabelRemoved => $"label {Label()} removed",
                AutomationCatalog.AssigneesChanged => "assignees changed",
                AutomationCatalog.ChecklistDone => "checklist items ticked",
                AutomationCatalog.CommentAdded => "comments added",
                _ => null,
            };
        }
    }
}
