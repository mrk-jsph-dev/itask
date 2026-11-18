using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.RemoveMember;

/// <summary>
/// Removes a member (Owner) or leaves the board (any member removing themselves).
/// The last Owner can't be removed or leave. Leaving also clears the caller's star and their card assignments on the board.
/// </summary>
public static class RemoveMember
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/boards/{boardId:guid}/members/{userId:guid}", Handle)
                .WithTags("Boards")
                .RequireAuthorization();

        private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Handle(
            Guid boardId, Guid userId, AppDbContext db, IBoardAccess access, ICurrentUser user, CancellationToken ct)
        {
            var role = await access.GetRoleAsync(boardId, ct);
            if (role is null)
            {
                return TypedResults.NotFound();
            }

            if (role < BoardRole.Owner && userId != user.Id)
            {
                return BoardResults.Forbidden();
            }

            var member = await db.BoardMembers.FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == userId, ct);
            if (member is null)
            {
                return TypedResults.NotFound();
            }

            if (member.Role == BoardRole.Owner
                && await db.BoardMembers.CountAsync(m => m.BoardId == boardId && m.Role == BoardRole.Owner, ct) == 1)
            {
                return BoardResults.Conflict("A board needs at least one Owner.");
            }

            db.BoardMembers.Remove(member);
            db.BoardStars.RemoveRange(
                await db.BoardStars.Where(s => s.BoardId == boardId && s.UserId == userId).ToListAsync(ct));
            db.CardAssignees.RemoveRange(
                await db.CardAssignees.Where(a => a.UserId == userId && a.Card!.BoardId == boardId).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
            // activity: member removed

            return TypedResults.NoContent();
        }
    }
}
