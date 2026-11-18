using FluentValidation;
using ITask.Api.Common.Activity;
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
            if (NeighbourSlot.Find(lists, l => l.Id, request.BeforeId, request.AfterId) is not { } index)
            {
                return BoardResults.Conflict("The board's lists changed. Reload and try again.");
            }

            NeighbourSlot.Place(lists, moved, index, l => l.Position, (l, p) => l.Position = p);
            activity.Record(ActivityTypes.ListMoved, boardId, null, new { list = moved.Name });

            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(new Response(moved.Id, moved.Position));
        }
    }
}
