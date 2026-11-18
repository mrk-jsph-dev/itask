using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Invites.RevokeInvite;

/// <summary>Deletes an invite so its link stops working. Owner only.</summary>
public static class RevokeInvite
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/boards/{boardId:guid}/invites/{inviteId:guid}", Handle)
                .WithTags("Invites")
                .RequireAuthorization();

        private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Handle(
            Guid boardId, Guid inviteId, AppDbContext db, IBoardAccess access, CancellationToken ct)
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

            var deleted = await db.BoardInvites
                .Where(i => i.Id == inviteId && i.BoardId == boardId)
                .ExecuteDeleteAsync(ct);

            return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        }
    }
}
