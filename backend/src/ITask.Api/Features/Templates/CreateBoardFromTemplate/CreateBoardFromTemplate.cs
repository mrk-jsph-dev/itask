using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Templates;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Templates;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Templates.CreateBoardFromTemplate;

/// <summary>
/// Creates a board from a built-in template (slug id) or one of the caller's saved templates (GUID id)
/// and makes the caller its Owner. Emoji and colour default to the template's.
/// </summary>
public static class CreateBoardFromTemplate
{
    public sealed record Request(string Name, string? Emoji = null, string? Description = null, string? Color = null);

    public sealed record Response(Guid Id, string Name, string? Emoji, string? Description, string Color, BoardRole Role);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(120);
            RuleFor(r => r.Emoji).MaximumLength(16);
            RuleFor(r => r.Description).MaximumLength(500);
            RuleFor(r => r.Color).HexColor().When(r => r.Color is not null);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/templates/{templateId}/boards", Handle)
                .WithTags("Templates")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Created<Response>, NotFound>> Handle(
            string templateId,
            Request request,
            AppDbContext db,
            ICurrentUser user,
            ITemplateCatalog catalog,
            IActivityRecorder activity,
            CancellationToken ct)
        {
            (string Name, string? Emoji, string Color, TemplateContent Content)? template = null;
            if (catalog.Find(templateId) is { } builtIn)
            {
                template = (builtIn.Name, builtIn.Emoji, builtIn.Color, builtIn.Content);
            }
            else if (Guid.TryParse(templateId, out var id)
                && await db.BoardTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == user.Id, ct) is { } saved)
            {
                template = (saved.Name, saved.Emoji, saved.Color, TemplateContent.Parse(saved.Content));
            }

            if (template is not { } source)
            {
                return TypedResults.NotFound();
            }

            var name = request.Name.Trim();
            var board = new Board
            {
                Name = name,
                KeyPrefix = CardKeys.DefaultPrefix(name),
                Emoji = string.IsNullOrWhiteSpace(request.Emoji) ? source.Emoji : request.Emoji,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Color = (request.Color ?? source.Color).ToUpperInvariant(),
            };
            board.Members.Add(new BoardMember { UserId = user.Id, Role = BoardRole.Owner });
            source.Content.ApplyTo(board, user.Id);
            if (board.Lists.Count == 0)
            {
                board.Lists.AddRange(BoardList.CreateDefaults());
            }

            db.Boards.Add(board);
            activity.Record(ActivityTypes.BoardCreated, board.Id, null, new { name = board.Name, template = source.Name });
            await db.SaveChangesAsync(ct);

            return TypedResults.Created(
                $"/api/boards/{board.Id}",
                new Response(board.Id, board.Name, board.Emoji, board.Description, board.Color, BoardRole.Owner));
        }
    }
}
