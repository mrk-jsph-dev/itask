using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.CompleteCard;

/// <summary>Marks a card complete or reopens it. Editor or above; both are idempotent.</summary>
public static class CompleteCard
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app)
        {
            app.MapPost("/boards/{boardId:guid}/cards/{cardId:guid}/complete", Complete)
                .WithTags("Cards")
                .RequireAuthorization();

            app.MapPost("/boards/{boardId:guid}/cards/{cardId:guid}/reopen", Reopen)
                .WithTags("Cards")
                .RequireAuthorization();
        }

        private static Task<Results<Ok<CardSummary>, ProblemHttpResult>> Complete(
            Guid boardId, Guid cardId, AppDbContext db, IBoardAccess access, TimeProvider clock, CancellationToken ct)
            => SetCompletedAsync(boardId, cardId, clock.GetUtcNow(), db, access, ct);

        private static Task<Results<Ok<CardSummary>, ProblemHttpResult>> Reopen(
            Guid boardId, Guid cardId, AppDbContext db, IBoardAccess access, CancellationToken ct)
            => SetCompletedAsync(boardId, cardId, null, db, access, ct);

        private static async Task<Results<Ok<CardSummary>, ProblemHttpResult>> SetCompletedAsync(
            Guid boardId, Guid cardId, DateTimeOffset? completedAt, AppDbContext db, IBoardAccess access, CancellationToken ct)
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

            if ((card.CompletedAt is null) != (completedAt is null))
            {
                card.CompletedAt = completedAt;
                await db.SaveChangesAsync(ct);

                // activity: card completed / reopened
            }

            return TypedResults.Ok((await CardSummaries.FindAsync(db, boardId, cardId, ct))!);
        }
    }
}
