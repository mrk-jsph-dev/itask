using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Automations.CreateAutomation;

/// <summary>
/// Adds a doorway or rule to the board. Owner only; at most <see cref="AutomationRule.MaxPerBoard"/>
/// per board. Every list, label and member it names must be on the board.
/// </summary>
public static class CreateAutomation
{
    public sealed record Request(
        AutomationKind Kind,
        string Name,
        Guid? ListId,
        DoorwayDirection? Direction,
        DoorwayMode? Mode,
        TriggerSpec? Trigger,
        IReadOnlyList<ConditionSpec>? Conditions,
        IReadOnlyList<ActionSpec>? Actions);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Kind).IsInEnum();
            RuleFor(r => r.Name).NotEmpty().MaximumLength(AutomationRule.NameMaxLength);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/automations", Handle)
                .WithTags("Automations")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Created<AutomationView>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId, Request request, AppDbContext db, IBoardAccess access, ICurrentUser user, IActivityRecorder activity,
            TimeProvider clock, CancellationToken ct)
        {
            if (await AutomationGuard.CheckAsync(boardId, BoardRole.Owner, true, db, access, ct) is { } denied)
            {
                return denied;
            }

            if (await db.AutomationRules.CountAsync(r => r.BoardId == boardId, ct) >= AutomationRule.MaxPerBoard)
            {
                return BoardResults.Conflict($"A board can have up to {AutomationRule.MaxPerBoard} automations. Delete one you no longer use first.");
            }

            var (spec, errors) = await AutomationGuard.ValidateAsync(
                db, boardId, request.Kind, request.ListId, request.Direction, request.Mode,
                request.Trigger, request.Conditions, request.Actions, ct);
            if (errors is not null)
            {
                return TypedResults.ValidationProblem(errors);
            }

            var rule = new AutomationRule
            {
                BoardId = boardId,
                Kind = request.Kind,
                Name = request.Name.Trim(),
                ListId = request.ListId,
                Direction = request.Direction,
                Mode = request.Mode,
                CreatedById = user.Id,
                CreatedAt = clock.GetUtcNow(),
            };
            spec!.WriteTo(rule);
            db.AutomationRules.Add(rule);
            activity.Record(ActivityTypes.AutomationCreated, boardId, null, new { rule = rule.Name, kind = rule.Kind.ToString() });
            await db.SaveChangesAsync(ct);

            var board = await AutomationBoards.LoadAsync(db, boardId, ct);
            return TypedResults.Created($"/api/boards/{boardId}/automations/{rule.Id}", AutomationView.From(rule, board));
        }
    }
}
