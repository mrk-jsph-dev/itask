using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.RemoveBlocker;

/// <summary>Stops a card waiting on another one. Editor or above; idempotent.</summary>
public static class RemoveBlocker
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/boards/{boardId:guid}/cards/{cardId:guid}/blocked-by/{blockerId:guid}", Handle)
                .WithTags("Cards")
                .RequireAuthorization();

        private static async Task<Results<Ok<CardSummary>, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Guid blockerId, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            if (!await CardGuard.CardExistsAsync(db, boardId, cardId, ct))
            {
                return CardGuard.NotFound();
            }

            var link = await db.CardDependencies
                .Include(d => d.BlockedBy)
                .FirstOrDefaultAsync(d => d.CardId == cardId && d.BlockedById == blockerId, ct);
            if (link is not null)
            {
                var prefix = await db.Boards.Where(b => b.Id == boardId).Select(b => b.KeyPrefix).FirstAsync(ct);
                db.CardDependencies.Remove(link);
                await activity.RecordCardAsync(ActivityTypes.CardDependencyRemoved, boardId, cardId, new
                {
                    blocker = new { id = blockerId, key = Card.Key(prefix, link.BlockedBy!.Number), title = link.BlockedBy.Title },
                }, ct);
                await db.SaveChangesAsync(ct);
            }

            return TypedResults.Ok((await CardSummaries.FindAsync(db, boardId, cardId, ct))!);
        }
    }
}
