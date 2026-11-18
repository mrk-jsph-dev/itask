using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Lists.MoveList;

/// <summary>
/// Moves a list between two neighbours. Editor or above. <c>BeforeId</c> is the list that ends
/// up immediately before the moved one, <c>AfterId</c> the one immediately after; give one or
/// both (neither moves it to the end). Neighbours that are no longer adjacent mean the client's
/// order is stale and the response is 409. Positions are renumbered when the gap gets too small.
/// </summary>
public static class MoveList
{
    public sealed record Request(Guid? BeforeId, Guid? AfterId);

    public sealed record Response(Guid Id, double Position);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.AfterId).NotEqual(r => r.BeforeId).When(r => r.AfterId is not null);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/lists/{listId:guid}/position", Handle)
                .WithTags("Lists")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, NotFound, ProblemHttpResult>> Handle(
            Guid boardId, Guid listId, Request request, AppDbContext db, IBoardAccess access, CancellationToken ct)
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

            var lists = await db.Lists
                .Where(l => l.BoardId == boardId && l.ArchivedAt == null)
                .OrderBy(l => l.Position)
                .ToListAsync(ct);

            var moved = lists.Find(l => l.Id == listId);
            if (moved is null)
            {
                return TypedResults.NotFound();
            }

            if (request.BeforeId == listId || request.AfterId == listId)
            {
                return BoardResults.Conflict("A list cannot be placed next to itself.");
            }

            lists.Remove(moved);
            var slot = FindSlot(lists, request);
            if (slot is null)
            {
                return BoardResults.Conflict("The board's lists changed. Reload and try again.");
            }

            var index = slot.Value;
            double? before = index > 0 ? lists[index - 1].Position : null;
            double? after = index < lists.Count ? lists[index].Position : null;

            if (PositionCalculator.NeedsRenumber(before, after))
            {
                lists.Insert(index, moved);
                var positions = PositionCalculator.Renumber(lists.Count);
                for (var i = 0; i < lists.Count; i++)
                {
                    lists[i].Position = positions[i];
                }
            }
            else
            {
                moved.Position = PositionCalculator.Between(before, after);
            }

            await db.SaveChangesAsync(ct);

            // activity: list moved

            return TypedResults.Ok(new Response(moved.Id, moved.Position));
        }

        /// <summary>
        /// Index in <paramref name="others"/> (the ordered lists without the moved one) where the
        /// moved list goes, or null when the neighbours are unknown or not adjacent.
        /// </summary>
        private static int? FindSlot(List<BoardList> others, Request request)
        {
            var beforeIndex = request.BeforeId is { } beforeId ? others.FindIndex(l => l.Id == beforeId) : -1;
            var afterIndex = request.AfterId is { } afterId ? others.FindIndex(l => l.Id == afterId) : others.Count;

            if ((request.BeforeId is not null && beforeIndex < 0) || (request.AfterId is not null && afterIndex < 0))
            {
                return null;
            }

            return (request.BeforeId, request.AfterId) switch
            {
                (null, null) => others.Count,
                (not null, null) => beforeIndex + 1,
                (null, not null) => afterIndex,
                _ => afterIndex == beforeIndex + 1 ? afterIndex : null,
            };
        }
    }
}
