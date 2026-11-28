using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.AddBlocker;

/// <summary>
/// Makes a card wait on another active card of the same board. Editor or above; idempotent. Refuses links
/// to the card itself (400), links that would close a loop (409 <c>dependency_cycle</c>) and more than
/// <see cref="CardDependency.MaxBlockersPerCard"/> blockers (409).
/// </summary>
public static class AddBlocker
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/cards/{cardId:guid}/blocked-by/{blockerId:guid}", Handle)
                .WithTags("Cards")
                .RequireAuthorization();

        private static async Task<Results<Ok<CardSummary>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Guid blockerId, AppDbContext db, IBoardAccess access, ICurrentUser user,
            IActivityRecorder activity, TimeProvider clock, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            if (!await CardGuard.CardExistsAsync(db, boardId, cardId, ct))
            {
                return CardGuard.NotFound();
            }

            if (blockerId == cardId)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["blockerId"] = ["A card can't wait on itself."],
                });
            }

            var blocker = await db.Cards.AsNoTracking()
                .Where(c => c.Id == blockerId && c.BoardId == boardId && c.ArchivedAt == null)
                .Select(c => new { c.Id, c.Number, c.Title })
                .FirstOrDefaultAsync(ct);
            if (blocker is null)
            {
                return CardGuard.NotFound();
            }

            if (!await db.CardDependencies.AnyAsync(d => d.CardId == cardId && d.BlockedById == blockerId, ct))
            {
                if (await Dependencies.CheckLimitAsync(db, cardId, ct) is { } full)
                {
                    return full;
                }

                if (await Dependencies.WouldCycleAsync(db, boardId, cardId, blockerId, ct))
                {
                    return Dependencies.Cycle();
                }

                var prefix = await db.Boards.Where(b => b.Id == boardId).Select(b => b.KeyPrefix).FirstAsync(ct);
                db.CardDependencies.Add(new CardDependency
                {
                    CardId = cardId,
                    BlockedById = blockerId,
                    CreatedById = user.Id,
                    CreatedAt = clock.GetUtcNow(),
                });
                await activity.RecordCardAsync(ActivityTypes.CardDependencyAdded, boardId, cardId, new
                {
                    blocker = new { id = blocker.Id, key = Card.Key(prefix, blocker.Number), title = blocker.Title },
                }, ct);

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // A concurrent request may have added the same link; anything else is a real failure.
                    db.ChangeTracker.Clear();
                    if (!await db.CardDependencies.AnyAsync(d => d.CardId == cardId && d.BlockedById == blockerId, ct))
                    {
                        throw;
                    }
                }
            }

            return TypedResults.Ok((await CardSummaries.FindAsync(db, boardId, cardId, ct))!);
        }
    }
}
