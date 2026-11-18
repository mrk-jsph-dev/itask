using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Json;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.UpdateCard;

/// <summary>
/// Changes the fields present in the body; absent fields stay, <c>null</c> clears. Editor or
/// above. Last write wins: moves renumber positions, so a row-version check here would reject
/// unrelated edits.
/// </summary>
public static class UpdateCard
{
    public sealed record Request
    {
        public Patch<string> Title { get; init; }

        public Patch<string?> Description { get; init; }

        public Patch<DateTimeOffset?> DueAt { get; init; }

        public Patch<DateTimeOffset?> StartAt { get; init; }

        public Patch<CardPriority> Priority { get; init; }

        public Patch<string?> CoverColor { get; init; }
    }

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Title.Value)
                .NotEmpty()
                .MaximumLength(Card.TitleMaxLength)
                .When(r => r.Title.HasValue)
                .OverridePropertyName(nameof(Request.Title));
            RuleFor(r => r.Description.Value)
                .MaximumLength(Card.DescriptionMaxLength)
                .When(r => r.Description.HasValue)
                .OverridePropertyName(nameof(Request.Description));
            RuleFor(r => r.Priority.Value)
                .IsInEnum()
                .When(r => r.Priority.HasValue)
                .OverridePropertyName(nameof(Request.Priority));
            RuleFor(r => r.CoverColor.Value)
                .HexColor()
                .When(r => r.CoverColor is { HasValue: true, Value: not null })
                .OverridePropertyName(nameof(Request.CoverColor));
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPatch("/boards/{boardId:guid}/cards/{cardId:guid}", Handle)
                .WithTags("Cards")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<CardSummary>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Request request, AppDbContext db, IBoardAccess access, CancellationToken ct)
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

            var dueAt = request.DueAt.Or(card.DueAt);
            var startAt = request.StartAt.Or(card.StartAt);
            if (dueAt is not null && startAt is not null && startAt > dueAt)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(Request.StartAt)] = ["The start date must be on or before the due date."],
                });
            }

            if (request.Title.HasValue)
            {
                card.Title = request.Title.Value.Trim();
            }

            if (request.Description.HasValue)
            {
                card.Description = string.IsNullOrWhiteSpace(request.Description.Value) ? null : request.Description.Value;
            }

            card.DueAt = dueAt;
            card.StartAt = startAt;
            card.Priority = request.Priority.Or(card.Priority);
            if (request.CoverColor.HasValue)
            {
                card.CoverColor = request.CoverColor.Value?.ToUpperInvariant();
            }

            await db.SaveChangesAsync(ct);

            // activity: card updated

            return TypedResults.Ok((await CardSummaries.FindAsync(db, boardId, cardId, ct))!);
        }
    }
}
