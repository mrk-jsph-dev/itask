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

namespace ITask.Api.Features.Labels.UpdateLabel;

/// <summary>Renames or recolours a label. Editor or above; names stay unique per board.</summary>
public static class UpdateLabel
{
    public sealed record Request(string Name, string Color, string? Emoji = null);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(Label.NameMaxLength);
            RuleFor(r => r.Color).NotEmpty().HexColor();
            RuleFor(r => r.Emoji).MaximumLength(16);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/labels/{labelId:guid}", Handle)
                .WithTags("Labels")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<LabelChip>, ProblemHttpResult>> Handle(
            Guid boardId, Guid labelId, Request request, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var label = await db.Labels.FirstOrDefaultAsync(l => l.Id == labelId && l.BoardId == boardId, ct);
            if (label is null)
            {
                return CardGuard.NotFound();
            }

            var name = request.Name.Trim();
            var others = await db.Labels
                .Where(l => l.BoardId == boardId && l.Id != labelId)
                .Select(l => l.Name)
                .ToListAsync(ct);
            if (others.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            {
                return BoardResults.Conflict("A label with this name already exists on the board.");
            }

            var color = request.Color.ToUpperInvariant();
            var emoji = string.IsNullOrWhiteSpace(request.Emoji) ? null : request.Emoji;
            if (label.Name != name || label.Color != color || label.Emoji != emoji)
            {
                activity.Record(ActivityTypes.LabelUpdated, boardId, null, new
                {
                    label = name,
                    emoji,
                    color,
                    from = label.Name != name ? label.Name : null,
                });
            }

            label.Name = name;
            label.Color = color;
            label.Emoji = emoji;
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(new LabelChip(label.Id, label.Name, label.Color, label.Emoji));
        }
    }
}
