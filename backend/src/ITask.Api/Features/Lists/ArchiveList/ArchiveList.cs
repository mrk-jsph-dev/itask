using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Lists.ArchiveList;

/// <summary>
/// Archives or restores a list. Editor or above; both are idempotent. A restored list goes to
/// the end of the board and must not clash with an active list's name.
/// </summary>
public static class ArchiveList
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app)
        {
            app.MapPost("/boards/{boardId:guid}/lists/{listId:guid}/archive", Archive)
                .WithTags("Lists")
                .RequireAuthorization();

            app.MapPost("/boards/{boardId:guid}/lists/{listId:guid}/restore", Restore)
                .WithTags("Lists")
                .RequireAuthorization();
        }

        private static Task<Results<NoContent, NotFound, ProblemHttpResult>> Archive(
            Guid boardId, Guid listId, AppDbContext db, IBoardAccess access, IActivityRecorder activity, TimeProvider clock, CancellationToken ct)
            => SetArchivedAsync(boardId, listId, clock.GetUtcNow(), db, access, activity, ct);

        private static Task<Results<NoContent, NotFound, ProblemHttpResult>> Restore(
            Guid boardId, Guid listId, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
            => SetArchivedAsync(boardId, listId, null, db, access, activity, ct);

        private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> SetArchivedAsync(
            Guid boardId, Guid listId, DateTimeOffset? archivedAt, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
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

            var list = await db.Lists.FirstOrDefaultAsync(l => l.Id == listId && l.BoardId == boardId, ct);
            if (list is null)
            {
                return TypedResults.NotFound();
            }

            if ((list.ArchivedAt is null) == (archivedAt is null))
            {
                return TypedResults.NoContent();
            }

            if (archivedAt is null)
            {
                var active = await db.Lists
                    .Where(l => l.BoardId == boardId && l.ArchivedAt == null)
                    .Select(l => new { l.Name, l.Position })
                    .ToListAsync(ct);

                if (active.Any(l => string.Equals(l.Name, list.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return BoardResults.Conflict("An active list already has this name. Rename it first.");
                }

                if (active.Count >= BoardList.MaxActivePerBoard)
                {
                    return BoardResults.Conflict($"A board can have at most {BoardList.MaxActivePerBoard} lists.");
                }

                list.Position = PositionCalculator.Append(active.Count == 0 ? null : active.Max(l => l.Position));
            }

            list.ArchivedAt = archivedAt;
            activity.Record(
                archivedAt is null ? ActivityTypes.ListRestored : ActivityTypes.ListArchived, boardId, null, new { list = list.Name });
            await db.SaveChangesAsync(ct);

            return TypedResults.NoContent();
        }
    }
}
