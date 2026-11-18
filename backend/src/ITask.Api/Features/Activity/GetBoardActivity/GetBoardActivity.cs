using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Activity.GetBoardActivity;

/// <summary>
/// A board's activity, newest first. Any member may read. <c>type</c> narrows to one category
/// (e.g. <c>card</c>, <c>comment</c>, <c>checklist</c>); <c>actorId</c> to one member.
/// </summary>
public static class GetBoardActivity
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/activity", Handle)
                .WithTags("Activity")
                .RequireAuthorization();

        private static async Task<Results<Ok<ActivityPage>, ProblemHttpResult>> Handle(
            Guid boardId,
            string? type,
            Guid? actorId,
            string? cursor,
            int? limit,
            AppDbContext db,
            IBoardAccess access,
            CancellationToken ct)
        {
            if (await access.GetRoleAsync(boardId, ct) is null)
            {
                return CardGuard.NotFound();
            }

            if (ActivityFeed.Validate(type, cursor) is { } invalid)
            {
                return invalid;
            }

            var query = db.Activity.Where(a => a.BoardId == boardId);
            if (actorId is { } actor)
            {
                query = query.Where(a => a.ActorId == actor);
            }

            return TypedResults.Ok(await ActivityFeed.PageAsync(query, type, cursor, limit, ct));
        }
    }
}
