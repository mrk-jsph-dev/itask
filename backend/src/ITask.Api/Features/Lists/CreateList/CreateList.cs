using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Lists.CreateList;

/// <summary>Adds a list at the end of the board. Editor or above.</summary>
public static class CreateList
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
            app.MapPost("/boards/{boardId:guid}/lists", Handle)
                .WithTags("Lists")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Created<Response>, NotFound, ProblemHttpResult>> Handle(
            Guid boardId, Request request, AppDbContext db, IBoardAccess access, CancellationToken ct)
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

            var name = request.Name.Trim();
            var active = await db.Lists
                .Where(l => l.BoardId == boardId && l.ArchivedAt == null)
                .Select(l => new { l.Name, l.Position })
                .ToListAsync(ct);

            if (active.Count >= BoardList.MaxActivePerBoard)
            {
                return BoardResults.Conflict($"A board can have at most {BoardList.MaxActivePerBoard} lists.");
            }

            if (active.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                return BoardResults.Conflict("A list with this name already exists on the board.");
            }

            var list = new BoardList
            {
                BoardId = boardId,
                Name = name,
                Emoji = string.IsNullOrWhiteSpace(request.Emoji) ? null : request.Emoji,
                WipLimit = request.WipLimit,
                Position = PositionCalculator.Append(active.Count == 0 ? null : active.Max(l => l.Position)),
            };
            db.Lists.Add(list);
            await db.SaveChangesAsync(ct);

            // activity: list created

            return TypedResults.Created(
                $"/api/boards/{boardId}/lists/{list.Id}",
                new Response(list.Id, list.Name, list.Emoji, list.Position, list.WipLimit));
        }
    }
}
