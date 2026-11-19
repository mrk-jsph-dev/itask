using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Automations.ToggleAutomation;

/// <summary>Turns an automation on or off. Owner only; idempotent. One that refers to something gone can't be turned on (400).</summary>
public static class ToggleAutomation
{
    public sealed record Request(bool Enabled);

    public sealed class Validator : AbstractValidator<Request>;

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/automations/{ruleId:guid}/enabled", Handle)
                .WithTags("Automations")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<AutomationView>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId, Guid ruleId, Request request, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
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

            var board = await AutomationBoards.LoadAsync(db, boardId, ct);
            if (request.Enabled && !rule.Enabled
                && AutomationCatalog.Validate(rule.Kind, rule.ListId, rule.Direction, rule.Mode, AutomationSpec.Of(rule), board) is { Count: > 0 } errors)
            {
                return TypedResults.ValidationProblem(errors);
            }

            if (rule.Enabled != request.Enabled)
            {
                rule.Enabled = request.Enabled;
                activity.Record(ActivityTypes.AutomationUpdated, boardId, null, new { rule = rule.Name, kind = rule.Kind.ToString(), enabled = rule.Enabled });
                await db.SaveChangesAsync(ct);
            }

            return TypedResults.Ok(AutomationView.From(rule, board));
        }
    }
}
