using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.SetCardLabels;

/// <summary>Replaces a card's labels. Editor or above. Every label must belong to the board.</summary>
public static class SetCardLabels
{
    public sealed record Request(IReadOnlyList<Guid> LabelIds);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.LabelIds).NotNull();
            RuleFor(r => r.LabelIds.Distinct().Count())
                .LessThanOrEqualTo(Card.MaxLabelsOrAssignees)
                .When(r => r.LabelIds is not null)
                .OverridePropertyName(nameof(Request.LabelIds))
                .WithMessage($"A card can have at most {Card.MaxLabelsOrAssignees} labels.");
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/cards/{cardId:guid}/labels", Handle)
                .WithTags("Cards")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<CardSummary>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Request request, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var card = await db.Cards
                .Include(c => c.Labels)
                .FirstOrDefaultAsync(c => c.Id == cardId && c.BoardId == boardId, ct);
            if (card is null)
            {
                return CardGuard.NotFound();
            }

            var wanted = request.LabelIds.Distinct().ToList();
            var known = await db.Labels.CountAsync(l => l.BoardId == boardId && wanted.Contains(l.Id), ct);
            if (known != wanted.Count)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(Request.LabelIds)] = ["Every label must belong to this board."],
                });
            }

            card.Labels.RemoveAll(l => !wanted.Contains(l.LabelId));
            card.Labels.AddRange(wanted
                .Where(id => card.Labels.All(l => l.LabelId != id))
                .Select(id => new CardLabel { CardId = cardId, LabelId = id }));
            await db.SaveChangesAsync(ct);

            // activity: card labels changed

            return TypedResults.Ok((await CardSummaries.FindAsync(db, boardId, cardId, ct))!);
        }
    }
}
