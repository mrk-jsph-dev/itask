using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Json;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Checklist.UpdateChecklistItem;

/// <summary>Changes an item's text, due date or assignee; absent fields stay, <c>null</c> clears. Editor or above.</summary>
public static class UpdateChecklistItem
{
    public sealed record Request
    {
        public Patch<string> Text { get; init; }

        public Patch<DateTimeOffset?> DueAt { get; init; }

        public Patch<Guid?> AssigneeId { get; init; }
    }

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Text.Value)
                .NotEmpty()
                .MaximumLength(ChecklistItem.TextMaxLength)
                .When(r => r.Text.HasValue)
                .OverridePropertyName(nameof(Request.Text));
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPatch("/boards/{boardId:guid}/cards/{cardId:guid}/checklist/{itemId:guid}", Handle)
                .WithTags("Checklist")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<ChecklistItemView>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Guid itemId, Request request, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var item = await db.ChecklistItems
                .FirstOrDefaultAsync(i => i.Id == itemId && i.CardId == cardId && i.Card!.BoardId == boardId, ct);
            if (item is null)
            {
                return CardGuard.NotFound();
            }

            if (request.AssigneeId is { HasValue: true, Value: { } assigneeId }
                && !await db.BoardMembers.AnyAsync(m => m.BoardId == boardId && m.UserId == assigneeId, ct))
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(Request.AssigneeId)] = ["The assignee must be a member of this board."],
                });
            }

            var before = new { item.Text, item.DueAt, item.AssigneeId };
            if (request.Text.HasValue)
            {
                item.Text = request.Text.Value.Trim();
            }

            item.DueAt = request.DueAt.Or(item.DueAt);
            item.AssigneeId = request.AssigneeId.Or(item.AssigneeId);
            if (before.Text != item.Text || before.DueAt != item.DueAt || before.AssigneeId != item.AssigneeId)
            {
                await activity.RecordCardAsync(ActivityTypes.ChecklistUpdated, boardId, cardId, new
                {
                    item = item.Text,
                    from = before.Text != item.Text ? before.Text : null,
                }, ct);
            }

            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(ChecklistItemView.From(item));
        }
    }
}
