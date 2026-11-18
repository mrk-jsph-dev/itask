using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.ArchiveCard;

/// <summary>
/// Archives or restores a card and returns it. Editor or above; both are idempotent. A restored
/// card goes to the end of its list, which must be active.
/// </summary>
public static class ArchiveCard
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app)
        {
            app.MapPost("/boards/{boardId:guid}/cards/{cardId:guid}/archive", Archive)
                .WithTags("Cards")
                .RequireAuthorization();

            app.MapPost("/boards/{boardId:guid}/cards/{cardId:guid}/restore", Restore)
                .WithTags("Cards")
                .RequireAuthorization();
        }

        private static Task<Results<Ok<CardSummary>, ProblemHttpResult>> Archive(
            Guid boardId, Guid cardId, AppDbContext db, IBoardAccess access, TimeProvider clock, CancellationToken ct)
            => SetArchivedAsync(boardId, cardId, clock.GetUtcNow(), db, access, ct);

        private static Task<Results<Ok<CardSummary>, ProblemHttpResult>> Restore(
            Guid boardId, Guid cardId, AppDbContext db, IBoardAccess access, CancellationToken ct)
            => SetArchivedAsync(boardId, cardId, null, db, access, ct);

        private static async Task<Results<Ok<CardSummary>, ProblemHttpResult>> SetArchivedAsync(
            Guid boardId, Guid cardId, DateTimeOffset? archivedAt, AppDbContext db, IBoardAccess access, CancellationToken ct)
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

            if ((card.ArchivedAt is null) != (archivedAt is null))
            {
                if (archivedAt is null)
                {
                    if (await db.Lists.AnyAsync(l => l.Id == card.ListId && l.ArchivedAt != null, ct))
                    {
                        return BoardResults.Conflict("This card's list is archived. Restore the list first.");
                    }

                    var last = await db.Cards
                        .Where(c => c.ListId == card.ListId && c.ArchivedAt == null)
                        .MaxAsync(c => (double?)c.Position, ct);
                    card.Position = PositionCalculator.Append(last);
                }

                card.ArchivedAt = archivedAt;
                await db.SaveChangesAsync(ct);

                // activity: card archived / restored
            }

            return TypedResults.Ok((await CardSummaries.FindAsync(db, boardId, cardId, ct))!);
        }
    }
}
