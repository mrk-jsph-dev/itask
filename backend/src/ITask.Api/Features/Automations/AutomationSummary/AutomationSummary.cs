using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Automations.AutomationSummary;

/// <summary>The board's automation receipt for the last 7 days. Editor or above. Undone runs don't count.</summary>
public static class AutomationSummary
{
    public const int Days = 7;

    public sealed record Response(int Days, int Ran, int Blocked, string Sentence, IReadOnlyList<AutomationTally> Rules);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/automations/summary", Handle)
                .WithTags("Automations")
                .RequireAuthorization();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId, AppDbContext db, IBoardAccess access, TimeProvider clock, CancellationToken ct)
        {
            if (await AutomationGuard.CheckAsync(boardId, BoardRole.Editor, false, db, access, ct) is { } denied)
            {
                return denied;
            }

            var receipt = (await AutomationReceipts.ForBoardsAsync(db, [boardId], clock.GetUtcNow().AddDays(-Days), ct)).FirstOrDefault();
            return TypedResults.Ok(receipt is null
                ? new Response(Days, 0, 0, "", [])
                : new Response(Days, receipt.Ran, receipt.Blocked, receipt.Sentence(), receipt.Rules));
        }
    }
}
