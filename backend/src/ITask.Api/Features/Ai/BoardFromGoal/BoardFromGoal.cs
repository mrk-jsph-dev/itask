using FluentValidation;
using ITask.Api.Common.Ai;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Validation;
using ITask.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Ai.BoardFromGoal;

/// <summary>
/// Drafts a board (name, emoji, lists, starter cards with checklists) from a goal. Nothing is saved;
/// the client creates the board through <c>POST /api/boards/from-content</c> after the user confirms.
/// </summary>
public static class BoardFromGoal
{
    public const int GoalMaxLength = 300;

    public sealed record Request(string Goal);

    public sealed record DraftCard(string List, string Title, IReadOnlyList<string> Checklist);

    public sealed record Response(string Name, string? Emoji, IReadOnlyList<string> Lists, IReadOnlyList<DraftCard> Cards, int Remaining);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.Goal).NotEmpty().MaximumLength(GoalMaxLength);
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/ai/board-from-goal", Handle)
                .RequireAi()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, ValidationProblem, ProblemHttpResult>> Handle(
            Request request, AiGateway gateway, CancellationToken ct)
        {
            var outcome = await gateway.RunAsync(
                AiPrompts.BoardFromGoal, "Goal:\n" + request.Goal.Trim(), 1_200, AiDrafts.ParseBoardFromGoal, ct, AiDrafts.BoardFromGoalSchema);
            if (outcome.Problem is { } problem)
            {
                return problem;
            }

            var draft = outcome.Value!;
            return TypedResults.Ok(new Response(
                draft.Name,
                draft.Emoji,
                draft.Lists,
                [.. draft.Cards.Select(c => new DraftCard(c.List, c.Title, c.Checklist))],
                outcome.Remaining));
        }
    }
}
