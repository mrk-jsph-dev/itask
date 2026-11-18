using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Checklist.AddChecklistItem;

/// <summary>Appends an item to a card's checklist. Editor or above; the assignee must be a board member.</summary>
public static class AddChecklistItem
{
    public sealed record Request(string Text, DateTimeOffset? DueAt = null, Guid? AssigneeId = null);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.Text).NotEmpty().MaximumLength(ChecklistItem.TextMaxLength);
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/cards/{cardId:guid}/checklist", Handle)
                .WithTags("Checklist")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Created<ChecklistItemView>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Request request, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            if (!await CardGuard.CardExistsAsync(db, boardId, cardId, ct))
            {
                return CardGuard.NotFound();
            }

            if (request.AssigneeId is { } assigneeId
                && !await db.BoardMembers.AnyAsync(m => m.BoardId == boardId && m.UserId == assigneeId, ct))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(Request.AssigneeId)] = ["The assignee must be a member of this board."],
                });
            }

            var positions = await db.ChecklistItems.Where(i => i.CardId == cardId).Select(i => i.Position).ToListAsync(ct);
            if (positions.Count >= ChecklistItem.MaxPerCard)
            {
                return BoardResults.Conflict($"A checklist can have at most {ChecklistItem.MaxPerCard} items.");
            }

            var item = new ChecklistItem
            {
                CardId = cardId,
                Text = request.Text.Trim(),
                DueAt = request.DueAt,
                AssigneeId = request.AssigneeId,
                Position = PositionCalculator.Append(positions.Count == 0 ? null : positions.Max()),
            };
            db.ChecklistItems.Add(item);
            await db.SaveChangesAsync(ct);

            // activity: checklist item added

            return TypedResults.Created(
                $"/api/boards/{boardId}/cards/{cardId}/checklist/{item.Id}", ChecklistItemView.From(item));
        }
    }
}
