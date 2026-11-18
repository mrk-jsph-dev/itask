using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.ArchiveBoard;

/// <summary>Archives or restores a board. Owner only; both are idempotent.</summary>
public static class ArchiveBoard
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app)
        {
            app.MapPost("/boards/{boardId:guid}/archive", Archive)
                .WithTags("Boards")
                .RequireAuthorization();

            app.MapPost("/boards/{boardId:guid}/restore", Restore)
                .WithTags("Boards")
                .RequireAuthorization();
        }

        private static Task<Results<NoContent, NotFound, ProblemHttpResult>> Archive(
            Guid boardId, AppDbContext db, IBoardAccess access, TimeProvider clock, CancellationToken ct)
            => SetArchivedAsync(boardId, clock.GetUtcNow(), db, access, ct);

        private static Task<Results<NoContent, NotFound, ProblemHttpResult>> Restore(
            Guid boardId, AppDbContext db, IBoardAccess access, CancellationToken ct)
            => SetArchivedAsync(boardId, null, db, access, ct);

        private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> SetArchivedAsync(
            Guid boardId, DateTimeOffset? archivedAt, AppDbContext db, IBoardAccess access, CancellationToken ct)
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

            var board = await db.Boards.FirstAsync(b => b.Id == boardId, ct);
            if ((board.ArchivedAt is null) != (archivedAt is null))
            {
                board.ArchivedAt = archivedAt;
                await db.SaveChangesAsync(ct);
                // activity: board archived / restored
            }

            return TypedResults.NoContent();
        }
    }
}
