using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Checklist.ToggleChecklistItem;

/// <summary>Sets an item done or not done. Editor or above. Idempotent.</summary>
public static class ToggleChecklistItem
{
    public sealed record Request(bool IsDone);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/cards/{cardId:guid}/checklist/{itemId:guid}/done", Handle)
                .WithTags("Checklist")
                .RequireAuthorization();

        private static async Task<Results<Ok<ChecklistItemView>, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Guid itemId, Request request, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var item = await db.ChecklistItems
                .FirstOrDefaultAsync(i => i.Id == itemId && i.CardId == cardId && i.Card!.BoardId == boardId, ct);
            if (item is null)
            {
                return CardGuard.NotFound();
            }

            if (item.IsDone != request.IsDone)
            {
                item.IsDone = request.IsDone;
                await activity.RecordCardAsync(
                    item.IsDone ? ActivityTypes.ChecklistChecked : ActivityTypes.ChecklistUnchecked,
                    boardId,
                    cardId,
                    new { item = item.Text },
                    ct);
                await db.SaveChangesAsync(ct);
            }

            return TypedResults.Ok(ChecklistItemView.From(item));
        }
    }
}
