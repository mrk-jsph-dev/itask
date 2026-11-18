using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.GetBoard;

public static class GetBoard
{
    /// <param name="RowVersion">Base64 concurrency token; send it back with UpdateBoard.</param>
    public sealed record Response(
        Guid Id,
        string Name,
        string? Emoji,
        string? Description,
        string Color,
        BoardRole Role,
        bool IsStarred,
        int MemberCount,
        DateTimeOffset? ArchivedAt,
        string KeyPrefix,
        string RowVersion);

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
            Guid boardId, AppDbContext db, IBoardAccess access, ICurrentUser user, CancellationToken ct)
        {
            var role = await access.GetRoleAsync(boardId, ct);
            if (role is null)
            {
                return TypedResults.NotFound();
            }

            var userId = user.Id;
            var board = await db.Boards
                .AsNoTracking()
                .Where(b => b.Id == boardId)
                .Select(b => new
                {
                    b.Id,
                    b.Name,
                    b.Emoji,
                    b.Description,
                    b.Color,
                    IsStarred = b.Stars.Any(s => s.UserId == userId),
                    MemberCount = b.Members.Count,
                    b.ArchivedAt,
                    b.KeyPrefix,
                    b.RowVersion,
                })
                .FirstAsync(ct);

            return TypedResults.Ok(new Response(
                board.Id,
                board.Name,
                board.Emoji,
                board.Description,
                board.Color,
                role.Value,
                board.IsStarred,
                board.MemberCount,
                board.ArchivedAt,
                board.KeyPrefix,
                Convert.ToBase64String(board.RowVersion)));
        }
    }
}
