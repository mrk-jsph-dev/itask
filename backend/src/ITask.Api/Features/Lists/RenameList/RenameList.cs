using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Lists.RenameList;

/// <summary>
/// Changes a list's name, emoji and WIP limit. Editor or above. Last write wins: moves
/// renumber positions, so a row-version check here would reject unrelated edits.
/// </summary>
public static class RenameList
{
    public sealed record Request(string Name, string? Emoji = null, int? WipLimit = null);

    public sealed record Response(Guid Id, string Name, string? Emoji, double Position, int? WipLimit);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(BoardList.NameMaxLength);
            RuleFor(r => r.Emoji).MaximumLength(16);
            RuleFor(r => r.WipLimit).InclusiveBetween(1, 999);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/lists/{listId:guid}", Handle)
                .WithTags("Lists")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, NotFound, ProblemHttpResult>> Handle(
            Guid boardId, Guid listId, Request request, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
        {
            var role = await access.GetRoleAsync(boardId, ct);
            if (role is null)
            {
                return TypedResults.NotFound();
            }

            if (role < BoardRole.Editor)
            {
                return BoardResults.Forbidden();
            }

            if (await db.Boards.AnyAsync(b => b.Id == boardId && b.ArchivedAt != null, ct))
            {
                return BoardResults.Archived();
            }

            var list = await db.Lists.FirstOrDefaultAsync(l => l.Id == listId && l.BoardId == boardId, ct);
            if (list is null)
            {
                return TypedResults.NotFound();
            }

            var name = request.Name.Trim();
            if (list.ArchivedAt is null)
            {
                var others = await db.Lists
                    .Where(l => l.BoardId == boardId && l.ArchivedAt == null && l.Id != listId)
                    .Select(l => l.Name)
                    .ToListAsync(ct);
                if (others.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return BoardResults.Conflict("A list with this name already exists on the board.");
                }
            }

            var emoji = string.IsNullOrWhiteSpace(request.Emoji) ? null : request.Emoji;
            if (list.Name != name)
            {
                activity.Record(ActivityTypes.ListRenamed, boardId, null, new { list = name, from = list.Name });
            }
            else if (list.Emoji != emoji || list.WipLimit != request.WipLimit)
            {
                activity.Record(ActivityTypes.ListUpdated, boardId, null, new { list = name });
            }

            list.Name = name;
            list.Emoji = emoji;
            list.WipLimit = request.WipLimit;
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(new Response(list.Id, list.Name, list.Emoji, list.Position, list.WipLimit));
        }
    }
}
