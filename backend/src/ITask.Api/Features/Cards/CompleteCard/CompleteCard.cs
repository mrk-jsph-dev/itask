using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.CompleteCard;

/// <summary>
/// Marks a card complete or reopens it. Editor or above; both are idempotent. Completing a repeating
/// card also creates its next copy, retrying when a concurrent create took the board's next number.
/// </summary>
public static class CompleteCard
{
    private const int MaxAttempts = 3;

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
            Guid boardId, Guid cardId, AppDbContext db, IBoardAccess access, IActivityRecorder activity, TimeProvider clock, CancellationToken ct)
            => SetCompletedAsync(boardId, cardId, clock.GetUtcNow(), db, access, activity, ct);

        private static Task<Results<Ok<CardSummary>, ProblemHttpResult>> Reopen(
            Guid boardId, Guid cardId, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
            => SetCompletedAsync(boardId, cardId, null, db, access, activity, ct);

        private static async Task<Results<Ok<CardSummary>, ProblemHttpResult>> SetCompletedAsync(
            Guid boardId, Guid cardId, DateTimeOffset? completedAt, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            for (var attempt = 1; ; attempt++)
            {
                var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId && c.BoardId == boardId, ct);
                if (card is null)
                {
                    return CardGuard.NotFound();
                }

                if ((card.CompletedAt is null) == (completedAt is null))
                {
                    break;
                }

                card.CompletedAt = completedAt;
                await activity.RecordCardAsync(
                    completedAt is null ? ActivityTypes.CardReopened : ActivityTypes.CardCompleted, boardId, cardId, null, ct);

                // Completing a repeating card creates its next copy at once; the rule moves to the copy.
                if (completedAt is { } now && card.ArchivedAt is null
                    && await db.CardRecurrences.FirstOrDefaultAsync(r => r.CardId == cardId, ct) is { } recurrence)
                {
                    await Recurrences.SpawnNextAsync(db, activity, recurrence, now, ct);
                }

                try
                {
                    await db.SaveChangesAsync(ct);
                    break;
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
                {
                    db.ChangeTracker.Clear();
                }
                catch (DbUpdateConcurrencyException)
                {
                    return BoardResults.Conflict("The board is busy. Try again.");
                }
            }

            return TypedResults.Ok((await CardSummaries.FindAsync(db, boardId, cardId, ct))!);
        }
    }
}
