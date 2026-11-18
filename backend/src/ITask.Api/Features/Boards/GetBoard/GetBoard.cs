using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.GetBoard;

public static class GetBoard
{
    public sealed record Response(Guid Id, string Name, string? Emoji, BoardRole Role);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}", Handle)
                .WithTags("Boards")
                .RequireAuthorization();

        /// <remarks>
        /// Non-members get 404, not 403, so board ids can't be probed.
        /// </remarks>
        private static async Task<Results<Ok<Response>, NotFound>> Handle(
            Guid boardId, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            var role = await access.GetRoleAsync(boardId, ct);
            if (role is null)
            {
                return TypedResults.NotFound();
            }

            var board = await db.Boards.AsNoTracking().FirstAsync(b => b.Id == boardId, ct);
            return TypedResults.Ok(new Response(board.Id, board.Name, board.Emoji, role.Value));
        }
    }
}
