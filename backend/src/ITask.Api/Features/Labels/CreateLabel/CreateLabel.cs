using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Labels.CreateLabel;

/// <summary>Adds a label to the board. Editor or above; names are unique per board ignoring case.</summary>
public static class CreateLabel
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
            app.MapPost("/boards/{boardId:guid}/labels", Handle)
                .WithTags("Labels")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Created<LabelChip>, ProblemHttpResult>> Handle(
            Guid boardId, Request request, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var name = request.Name.Trim();
            var names = await db.Labels.Where(l => l.BoardId == boardId).Select(l => l.Name).ToListAsync(ct);
            if (names.Count >= Label.MaxPerBoard)
            {
                return BoardResults.Conflict($"A board can have at most {Label.MaxPerBoard} labels.");
            }

            if (names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            {
                return BoardResults.Conflict("A label with this name already exists on the board.");
            }

            var label = new Label
            {
                BoardId = boardId,
                Name = name,
                Color = request.Color.ToUpperInvariant(),
                Emoji = string.IsNullOrWhiteSpace(request.Emoji) ? null : request.Emoji,
            };
            db.Labels.Add(label);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created(
                $"/api/boards/{boardId}/labels/{label.Id}",
                new LabelChip(label.Id, label.Name, label.Color, label.Emoji));
        }
    }
}
