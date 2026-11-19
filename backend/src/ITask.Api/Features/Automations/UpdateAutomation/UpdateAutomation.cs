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

namespace ITask.Api.Features.Automations.UpdateAutomation;

/// <summary>
/// Replaces an automation's name and parts (its kind stays). Owner only. 409 when
/// <c>RowVersion</c> no longer matches. Saving a valid automation turns it back on.
/// </summary>
public static class UpdateAutomation
{
    public sealed record Request(
        string Name,
        Guid? ListId,
        DoorwayDirection? Direction,
        DoorwayMode? Mode,
        TriggerSpec? Trigger,
        IReadOnlyList<ConditionSpec>? Conditions,
        IReadOnlyList<ActionSpec>? Actions,
        string RowVersion);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(AutomationRule.NameMaxLength);
            RuleFor(r => r.RowVersion).RowVersion();
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/automations/{ruleId:guid}", Handle)
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

            ValidationRules.TryDecode(request.RowVersion, out var rowVersion);
            if (!rule.RowVersion.AsSpan().SequenceEqual(rowVersion))
            {
                return Stale();
            }

            var (spec, errors) = await AutomationGuard.ValidateAsync(
                db, boardId, rule.Kind, request.ListId, request.Direction, request.Mode,
                request.Trigger, request.Conditions, request.Actions, ct);
            if (errors is not null)
            {
                return TypedResults.ValidationProblem(errors);
            }

            rule.Name = request.Name.Trim();
            rule.ListId = request.ListId;
            rule.Direction = request.Direction;
            rule.Mode = request.Mode;
            rule.Enabled = true;
            spec!.WriteTo(rule);
            activity.Record(ActivityTypes.AutomationUpdated, boardId, null, new { rule = rule.Name, kind = rule.Kind.ToString() });

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Stale();
            }

            var board = await AutomationBoards.LoadAsync(db, boardId, ct);
            return TypedResults.Ok(AutomationView.From(rule, board));
        }

        private static ProblemHttpResult Stale() =>
            BoardResults.Conflict("The automation was changed by someone else. Reload and try again.");
    }
}
