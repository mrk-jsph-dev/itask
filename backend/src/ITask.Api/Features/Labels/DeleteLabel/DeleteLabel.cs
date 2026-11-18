using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Labels.DeleteLabel;

/// <summary>Deletes a label for good and removes it from every card. Editor or above.</summary>
public static class DeleteLabel
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/boards/{boardId:guid}/labels/{labelId:guid}", Handle)
                .WithTags("Labels")
                .RequireAuthorization();

        private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
            Guid boardId, Guid labelId, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var label = await db.Labels.FirstOrDefaultAsync(l => l.Id == labelId && l.BoardId == boardId, ct);
            if (label is null)
            {
                return CardGuard.NotFound();
            }

            db.CardLabels.RemoveRange(await db.CardLabels.Where(cl => cl.LabelId == labelId).ToListAsync(ct));
            db.Labels.Remove(label);
            await db.SaveChangesAsync(ct);

            return TypedResults.NoContent();
        }
    }
}
