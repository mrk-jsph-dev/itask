using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.SetRecurrence;

/// <summary>
/// Sets or clears a card's repeat rule. Editor or above. Occurrences follow the caller's profile
/// time zone; the first copy is due at midnight of the first occurrence after today.
/// </summary>
public static class SetRecurrence
{
    /// <param name="Weekdays">0 (Sunday) to 6; required for <see cref="RecurrenceKind.Weekly"/>.</param>
    /// <param name="DayOfMonth">1 to 31; required for <see cref="RecurrenceKind.Monthly"/>.</param>
    public sealed record Request(RecurrenceKind Kind, IReadOnlyList<int>? Weekdays, int? DayOfMonth, Guid TargetListId);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Kind).IsInEnum();
            RuleFor(r => r.TargetListId).NotEmpty();
            RuleFor(r => r.Weekdays)
                .NotEmpty()
                .Must(days => days!.All(d => d is >= 0 and <= 6)).WithMessage("Weekdays run from 0 (Sunday) to 6.")
                .When(r => r.Kind == RecurrenceKind.Weekly);
            RuleFor(r => r.DayOfMonth)
                .NotNull()
                .InclusiveBetween(1, 31)
                .When(r => r.Kind == RecurrenceKind.Monthly);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app)
        {
            app.MapPut("/boards/{boardId:guid}/cards/{cardId:guid}/recurrence", Set)
                .WithTags("Cards")
                .RequireAuthorization()
                .WithValidation<Request>();

            app.MapDelete("/boards/{boardId:guid}/cards/{cardId:guid}/recurrence", Clear)
                .WithTags("Cards")
                .RequireAuthorization();
        }

        private static async Task<Results<Ok<RecurrenceView>, ValidationProblem, ProblemHttpResult>> Set(
            Guid boardId,
            Guid cardId,
            Request request,
            AppDbContext db,
            IBoardAccess access,
            ICurrentUser user,
            IActivityRecorder activity,
            TimeProvider clock,
            CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var card = await db.Cards
                .AsNoTracking()
                .Where(c => c.Id == cardId && c.BoardId == boardId)
                .Select(c => new { c.ArchivedAt })
                .FirstOrDefaultAsync(ct);
            if (card is null)
            {
                return CardGuard.NotFound();
            }

            if (card.ArchivedAt is not null)
            {
                return BoardResults.Conflict("This card is archived. Restore it to change how it repeats.");
            }

            var listName = await db.Lists
                .Where(l => l.Id == request.TargetListId && l.BoardId == boardId && l.ArchivedAt == null)
                .Select(l => l.Name)
                .FirstOrDefaultAsync(ct);
            if (listName is null)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(Request.TargetListId)] = ["Pick an active list on this board."],
                });
            }

            var timeZone = await db.Users.Where(u => u.Id == user.Id).Select(u => u.TimeZone).FirstAsync(ct);
            var weekdays = request.Kind == RecurrenceKind.Weekly ? Recurrences.Mask(request.Weekdays!) : 0;
            var dayOfMonth = request.Kind == RecurrenceKind.Monthly ? request.DayOfMonth : null;
            var rule = new RecurrenceRule(request.Kind, weekdays, dayOfMonth);

            var recurrence = await db.CardRecurrences.FirstOrDefaultAsync(r => r.CardId == cardId, ct);
            if (recurrence is null)
            {
                recurrence = new CardRecurrence { CardId = cardId };
                db.CardRecurrences.Add(recurrence);
            }

            recurrence.Kind = request.Kind;
            recurrence.Weekdays = weekdays;
            recurrence.DayOfMonth = dayOfMonth;
            recurrence.TargetListId = request.TargetListId;
            recurrence.TimeZone = timeZone;
            recurrence.NextRunAt = Recurrences.FirstRun(rule, clock.GetUtcNow(), Recurrences.Zone(timeZone));
            recurrence.CreatedById = user.Id;

            await activity.RecordCardAsync(ActivityTypes.CardRecurrenceSet, boardId, cardId, new
            {
                kind = request.Kind.ToString(),
                weekdays = request.Kind == RecurrenceKind.Weekly ? Recurrences.Days(weekdays) : null,
                dayOfMonth,
                list = listName,
            }, ct);
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(RecurrenceView.From(recurrence));
        }

        private static async Task<Results<NoContent, ProblemHttpResult>> Clear(
            Guid boardId, Guid cardId, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            if (!await CardGuard.CardExistsAsync(db, boardId, cardId, ct))
            {
                return CardGuard.NotFound();
            }

            var recurrence = await db.CardRecurrences.FirstOrDefaultAsync(r => r.CardId == cardId, ct);
            if (recurrence is not null)
            {
                db.CardRecurrences.Remove(recurrence);
                await activity.RecordCardAsync(ActivityTypes.CardRecurrenceCleared, boardId, cardId, null, ct);
                await db.SaveChangesAsync(ct);
            }

            return TypedResults.NoContent();
        }
    }
}
