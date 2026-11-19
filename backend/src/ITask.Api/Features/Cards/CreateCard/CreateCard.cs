using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.CreateCard;

/// <summary>
/// Adds a card at the end of an active list. Editor or above. The card takes the board's next
/// number; the board's row version makes concurrent creates retry instead of sharing a number.
/// A block-mode doorway on the list returns 409 with <c>code: "doorway"</c>.
/// </summary>
public static class CreateCard
{
    private const int MaxAttempts = 5;

    public sealed record Request(string Title);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Title).NotEmpty().MaximumLength(Card.TitleMaxLength);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/lists/{listId:guid}/cards", Handle)
                .WithTags("Cards")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Created<CardSummary>, ProblemHttpResult>> Handle(
            Guid boardId, Guid listId, Request request, AppDbContext db, IBoardAccess access, ICurrentUser user, IActivityRecorder activity,
            TimeProvider clock, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var list = await db.Lists
                .AsNoTracking()
                .Where(l => l.Id == listId && l.BoardId == boardId)
                .Select(l => new { l.Name, l.ArchivedAt })
                .FirstOrDefaultAsync(ct);
            if (list is null)
            {
                return CardGuard.NotFound();
            }

            if (list.ArchivedAt is not null)
            {
                return BoardResults.Conflict("This list is archived. Restore it to add cards.");
            }

            if (await DataLimits.CheckCardsAsync(db, boardId, ct) is { } full)
            {
                return full;
            }

            if (await ListDoorways.CheckAsync(db, new Card { BoardId = boardId, Title = request.Title }, null, listId, ct) is { } blocked)
            {
                return await blocked.RecordAsync(db, Guid.Empty, user.Id, clock.GetUtcNow(), ct);
            }

            for (var attempt = 1; ; attempt++)
            {
                var board = await db.Boards.FirstAsync(b => b.Id == boardId, ct);
                var last = await db.Cards
                    .Where(c => c.ListId == listId && c.ArchivedAt == null)
                    .MaxAsync(c => (double?)c.Position, ct);

                var card = new Card
                {
                    BoardId = boardId,
                    ListId = listId,
                    Number = board.NextCardNumber,
                    Title = request.Title.Trim(),
                    Position = PositionCalculator.Append(last),
                    CreatedById = user.Id,
                };
                board.NextCardNumber++;
                db.Cards.Add(card);
                await activity.RecordCardAsync(ActivityTypes.CardCreated, boardId, card.Id, new { list = list.Name }, ct);

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
                {
                    // A short random pause keeps concurrent creates from colliding again in lockstep.
                    db.ChangeTracker.Clear();
                    await Task.Delay(Random.Shared.Next(5, 25 * attempt), ct);
                    continue;
                }
                catch (DbUpdateConcurrencyException)
                {
                    return BoardResults.Conflict("The board is busy. Try again.");
                }

                var summary = await CardSummaries.FindAsync(db, boardId, card.Id, ct);
                return TypedResults.Created($"/api/boards/{boardId}/cards/{card.Id}", summary!);
            }
        }
    }
}
