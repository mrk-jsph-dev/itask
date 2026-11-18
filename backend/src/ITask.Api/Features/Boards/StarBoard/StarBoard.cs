using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.StarBoard;

/// <summary>Stars (PUT) or unstars (DELETE) a board for the caller. Both are idempotent.</summary>
public static class StarBoard
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app)
        {
            app.MapPut("/boards/{boardId:guid}/star", Star)
                .WithTags("Boards")
                .RequireAuthorization();

            app.MapDelete("/boards/{boardId:guid}/star", Unstar)
                .WithTags("Boards")
                .RequireAuthorization();
        }

        private static async Task<Results<NoContent, NotFound>> Star(
            Guid boardId, AppDbContext db, IBoardAccess access, ICurrentUser user, CancellationToken ct)
        {
            if (!await access.HasAtLeastAsync(boardId, BoardRole.Viewer, ct))
            {
                return TypedResults.NotFound();
            }

            var userId = user.Id;
            if (!await db.BoardStars.AnyAsync(s => s.BoardId == boardId && s.UserId == userId, ct))
            {
                db.BoardStars.Add(new BoardStar { BoardId = boardId, UserId = userId });
                await db.SaveChangesAsync(ct);
            }

            return TypedResults.NoContent();
        }

        private static async Task<Results<NoContent, NotFound>> Unstar(
            Guid boardId, AppDbContext db, IBoardAccess access, ICurrentUser user, CancellationToken ct)
        {
            if (!await access.HasAtLeastAsync(boardId, BoardRole.Viewer, ct))
            {
                return TypedResults.NotFound();
            }

            var userId = user.Id;
            await db.BoardStars.Where(s => s.BoardId == boardId && s.UserId == userId).ExecuteDeleteAsync(ct);
            return TypedResults.NoContent();
        }
    }
}
