using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Templates;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Templates.SaveBoardAsTemplate;

/// <summary>
/// Saves a board's active lists, labels and (optionally) card skeletons as a template private to the
/// caller. Owner only. Members, comments, files, dates and assignees are never copied.
/// </summary>
public static class SaveBoardAsTemplate
{
    public sealed record Request(string Name, string? Description = null, bool IncludeCards = true);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(BoardTemplate.NameMaxLength);
            RuleFor(r => r.Description).MaximumLength(500);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/templates", Handle)
                .WithTags("Templates")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Created<TemplateSummary>, NotFound, ProblemHttpResult>> Handle(
            Guid boardId, Request request, AppDbContext db, IBoardAccess access, ICurrentUser user, CancellationToken ct)
        {
            var role = await access.GetRoleAsync(boardId, ct);
            if (role is null)
            {
                return TypedResults.NotFound();
            }

            if (role < BoardRole.Owner)
            {
                return BoardResults.Forbidden();
            }

            if (await db.BoardTemplates.CountAsync(t => t.OwnerId == user.Id, ct) >= BoardTemplate.MaxPerUser)
            {
                return BoardResults.Conflict($"You can keep up to {BoardTemplate.MaxPerUser} templates. Delete one first.");
            }

            var board = await db.Boards
                .AsNoTracking()
                .Where(b => b.Id == boardId)
                .Select(b => new { b.Emoji, b.Color })
                .FirstAsync(ct);
            var content = await TemplateContent.FromBoardAsync(db, boardId, request.IncludeCards, ct);
            var template = new BoardTemplate
            {
                OwnerId = user.Id,
                Name = request.Name.Trim(),
                Emoji = board.Emoji,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Color = board.Color,
                Content = content.Serialize(),
            };
            db.BoardTemplates.Add(template);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created($"/api/templates/{template.Id}", TemplateSummary.Of(template));
        }
    }
}
