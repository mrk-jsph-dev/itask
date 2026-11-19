using System.Text;
using FluentValidation;
using ITask.Api.Common.Ai;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Ai.DraftAutomation;

/// <summary>"Describe it": drafts one automation from plain words, using only the board's own lists, labels and members. Editor or above; nothing is saved.</summary>
public static class DraftAutomation
{
    public const int MaxTextLength = 500;

    public sealed record Request(string Text);

    public sealed record Response(AutomationDraft Automation, int Remaining);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Text).NotEmpty().MaximumLength(MaxTextLength);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/ai/automation", Handle)
                .RequireAi()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId, Request request, AppDbContext db, IBoardAccess access, AiGateway gateway, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var board = await AutomationBoards.LoadAsync(db, boardId, ct);
            var prompt = new StringBuilder()
                .Append("Lists: ").AppendJoin(", ", board.Lists.Values).Append('\n')
                .Append("Labels: ").AppendJoin(", ", board.Labels.Values).Append('\n')
                .Append("Members: ").AppendJoin(", ", board.Members.Values.Select(m => m.Name)).Append("\n\n")
                .Append("Request:\n").Append(request.Text.Trim())
                .ToString();

            var outcome = await gateway.RunAsync(
                AiPrompts.Automation, prompt, 500, json => AutomationDrafts.ParseAutomation(json, board), ct, AutomationDrafts.AutomationSchema);
            return outcome.Problem is { } problem
                ? problem
                : TypedResults.Ok(new Response(outcome.Value!, outcome.Remaining));
        }
    }
}
