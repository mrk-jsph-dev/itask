using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Checklist.DeleteChecklistItem;

/// <summary>Removes an item for good. Editor or above.</summary>
public static class DeleteChecklistItem
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/boards/{boardId:guid}/cards/{cardId:guid}/checklist/{itemId:guid}", Handle)
                .WithTags("Checklist")
                .RequireAuthorization();

        private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Guid itemId, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var deleted = await db.ChecklistItems
                .Where(i => i.Id == itemId && i.CardId == cardId && i.Card!.BoardId == boardId)
                .ExecuteDeleteAsync(ct);
            if (deleted == 0)
            {
                return CardGuard.NotFound();
            }

            // activity: checklist item deleted

            return TypedResults.NoContent();
        }
    }
}
