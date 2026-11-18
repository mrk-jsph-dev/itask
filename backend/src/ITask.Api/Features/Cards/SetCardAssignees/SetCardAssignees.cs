using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.SetCardAssignees;

/// <summary>Replaces a card's assignees. Editor or above. Every assignee must be a member of the board.</summary>
public static class SetCardAssignees
{
    public sealed record Request(IReadOnlyList<Guid> UserIds);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.UserIds).NotNull();
            RuleFor(r => r.UserIds.Distinct().Count())
                .LessThanOrEqualTo(Card.MaxLabelsOrAssignees)
                .When(r => r.UserIds is not null)
                .OverridePropertyName(nameof(Request.UserIds))
                .WithMessage($"A card can have at most {Card.MaxLabelsOrAssignees} assignees.");
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/cards/{cardId:guid}/assignees", Handle)
                .WithTags("Cards")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<CardSummary>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Request request, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var card = await db.Cards
                .Include(c => c.Assignees)
                .FirstOrDefaultAsync(c => c.Id == cardId && c.BoardId == boardId, ct);
            if (card is null)
            {
                return CardGuard.NotFound();
            }

            var wanted = request.UserIds.Distinct().ToList();
            var known = await db.BoardMembers.CountAsync(m => m.BoardId == boardId && wanted.Contains(m.UserId), ct);
            if (known != wanted.Count)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(Request.UserIds)] = ["Every assignee must be a member of this board."],
                });
            }

            var current = card.Assignees.Select(a => a.UserId).ToList();
            var added = wanted.Except(current).ToList();
            var removed = current.Except(wanted).ToList();
            if (added.Count + removed.Count > 0)
            {
                var changed = added.Concat(removed).ToList();
                var names = await db.Users
                    .Where(u => changed.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
                await activity.RecordCardAsync(ActivityTypes.CardAssigneesChanged, boardId, cardId, new
                {
                    added = added.Select(id => names[id]).ToList(),
                    removed = removed.Select(id => names[id]).ToList(),
                }, ct);
            }

            card.Assignees.RemoveAll(a => !wanted.Contains(a.UserId));
            card.Assignees.AddRange(wanted
                .Where(id => card.Assignees.All(a => a.UserId != id))
                .Select(id => new CardAssignee { CardId = cardId, UserId = id }));
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok((await CardSummaries.FindAsync(db, boardId, cardId, ct))!);
        }
    }
}
