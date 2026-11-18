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

namespace ITask.Api.Features.Checklist.MoveChecklistItem;

/// <summary>
/// Reorders an item within its checklist. Editor or above. <c>BeforeId</c> and <c>AfterId</c> are
/// the items that end up immediately before and after it (neither appends); 409 when they are not
/// adjacent. <c>Renumbered</c> says every item's position was reset.
/// </summary>
public static class MoveChecklistItem
{
    public sealed record Request(Guid? BeforeId, Guid? AfterId);

    public sealed record Response(ChecklistItemView Item, bool Renumbered);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.AfterId).NotEqual(r => r.BeforeId).When(r => r.AfterId is not null);
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/cards/{cardId:guid}/checklist/{itemId:guid}/position", Handle)
                .WithTags("Checklist")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Guid itemId, Request request, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var items = await db.ChecklistItems
                .Where(i => i.CardId == cardId && i.Card!.BoardId == boardId)
                .OrderBy(i => i.Position)
                .ToListAsync(ct);
            var item = items.FirstOrDefault(i => i.Id == itemId);
            if (item is null)
            {
                return CardGuard.NotFound();
            }

            items.Remove(item);
            if (NeighbourSlot.Find(items, i => i.Id, request.BeforeId, request.AfterId) is not { } index)
            {
                return BoardResults.Conflict("The checklist changed. Reload and try again.");
            }

            var renumbered = NeighbourSlot.Place(items, item, index, i => i.Position, (i, p) => i.Position = p);
            await activity.RecordCardAsync(ActivityTypes.ChecklistMoved, boardId, cardId, new { item = item.Text }, ct);
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(new Response(ChecklistItemView.From(item), renumbered));
        }
    }
}
