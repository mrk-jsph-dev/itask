using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.MoveCard;

/// <summary>
/// Moves a card within its list or to another active list. Editor or above. <c>BeforeId</c> and
/// <c>AfterId</c> are the cards that end up immediately before and after it in the target list
/// (neither appends). The response is 409 when <c>RowVersion</c> no longer matches (someone else
/// changed the card) or the neighbours are not adjacent (the client's order is stale).
/// <c>Renumbered</c> says the target list's positions were reset, so other cards' row versions changed.
/// </summary>
public static class MoveCard
{
    public sealed record Request(Guid ListId, Guid? BeforeId, Guid? AfterId, string RowVersion);

    public sealed record Response(CardSummary Card, bool Renumbered);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.ListId).NotEmpty();
            RuleFor(r => r.AfterId).NotEqual(r => r.BeforeId).When(r => r.AfterId is not null);
            RuleFor(r => r.RowVersion).RowVersion();
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/cards/{cardId:guid}/position", Handle)
                .WithTags("Cards")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Request request, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId && c.BoardId == boardId, ct);
            if (card is null)
            {
                return CardGuard.NotFound();
            }

            if (card.ArchivedAt is not null)
            {
                return BoardResults.Conflict("This card is archived. Restore it first.");
            }

            ValidationRules.TryDecode(request.RowVersion, out var rowVersion);
            if (!card.RowVersion.AsSpan().SequenceEqual(rowVersion))
            {
                return Stale();
            }

            var target = await db.Lists
                .AsNoTracking()
                .Where(l => l.Id == request.ListId && l.BoardId == boardId)
                .Select(l => new { l.Name, l.ArchivedAt })
                .FirstOrDefaultAsync(ct);
            if (target is null)
            {
                return CardGuard.NotFound();
            }

            if (target.ArchivedAt is not null)
            {
                return BoardResults.Conflict("That list is archived.");
            }

            if (request.BeforeId == cardId || request.AfterId == cardId)
            {
                return BoardResults.Conflict("A card cannot be placed next to itself.");
            }

            var others = await db.Cards
                .Where(c => c.ListId == request.ListId && c.ArchivedAt == null && c.Id != cardId)
                .OrderBy(c => c.Position)
                .ToListAsync(ct);

            if (NeighbourSlot.Find(others, c => c.Id, request.BeforeId, request.AfterId) is not { } index)
            {
                return BoardResults.Conflict("The list's cards changed. Reload and try again.");
            }

            if (card.ListId == request.ListId)
            {
                await activity.RecordCardAsync(ActivityTypes.CardMoved, boardId, cardId, new { list = target.Name }, ct);
            }
            else
            {
                var fromList = await db.Lists.Where(l => l.Id == card.ListId).Select(l => l.Name).FirstAsync(ct);
                await activity.RecordCardAsync(ActivityTypes.CardMoved, boardId, cardId, new { fromList, toList = target.Name }, ct);
            }

            card.ListId = request.ListId;
            var renumbered = NeighbourSlot.Place(others, card, index, c => c.Position, (c, p) => c.Position = p);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Stale();
            }

            var summary = await CardSummaries.FindAsync(db, boardId, cardId, ct);
            return TypedResults.Ok(new Response(summary!, renumbered));
        }

        private static ProblemHttpResult Stale() =>
            BoardResults.Conflict("The card was changed by someone else. Reload and try again.");
    }
}
