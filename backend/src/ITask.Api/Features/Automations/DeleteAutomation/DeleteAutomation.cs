using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Automations.DeleteAutomation;

/// <summary>Deletes an automation and its run history. Owner only. Changes it made stay.</summary>
public static class DeleteAutomation
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/boards/{boardId:guid}/automations/{ruleId:guid}", Handle)
                .WithTags("Automations")
                .RequireAuthorization();

        private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
            Guid boardId, Guid ruleId, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            if (await AutomationGuard.CheckAsync(boardId, BoardRole.Owner, true, db, access, ct) is { } denied)
            {
                return denied;
            }

            var rule = await db.AutomationRules.FirstOrDefaultAsync(r => r.Id == ruleId && r.BoardId == boardId, ct);
            if (rule is null)
            {
                return CardGuard.NotFound();
            }

            db.AutomationRules.Remove(rule);
            activity.Record(ActivityTypes.AutomationDeleted, boardId, null, new { rule = rule.Name, kind = rule.Kind.ToString() });
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }
    }
}
