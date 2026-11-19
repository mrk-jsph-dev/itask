using System.Text;
using ITask.Api.Common.Ai;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Ai.BreakDown;

/// <summary>Drafts checklist items for a card from its title, description and current checklist. Editor or above; nothing is saved.</summary>
public static class BreakDown
{
    public sealed record Response(IReadOnlyList<string> Items, int Remaining);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/cards/{cardId:guid}/ai/breakdown", Handle)
                .RequireAi();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, AppDbContext db, IBoardAccess access, AiGateway gateway, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var card = await db.Cards.AsNoTracking()
                .Where(c => c.Id == cardId && c.BoardId == boardId)
                .Select(c => new { c.Title, c.Description, Checklist = c.Checklist.OrderBy(i => i.Position).Select(i => i.Text).ToList() })
                .FirstOrDefaultAsync(ct);
            if (card is null)
            {
                return CardGuard.NotFound();
            }

            var prompt = new StringBuilder()
                .Append("Title: ").Append(card.Title).Append("\n\n")
                .Append("Description:\n").Append(AiText.Cut(card.Description, 6_000)).Append("\n\n")
                .Append("Existing checklist:\n").AppendJoin('\n', card.Checklist.Take(50).Select(i => "- " + AiText.Cut(i, 200)))
                .ToString();

            var outcome = await gateway.RunAsync(
                AiPrompts.Breakdown, prompt, 400, json => AiDrafts.ParseBreakdown(json, card.Checklist), ct, AiDrafts.BreakdownSchema);
            return outcome.Problem is { } problem
                ? problem
                : TypedResults.Ok(new Response(outcome.Value!, outcome.Remaining));
        }
    }
}
