using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Files;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Realtime;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.DeleteBoard;

/// <summary>
/// Permanently deletes an archived board with its members, stars, invites, cards and their files. Owner only.
/// </summary>
public static class DeleteBoard
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/boards/{boardId:guid}", Handle)
                .WithTags("Boards")
                .RequireAuthorization()
                .RequireNonDemo();

        private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Handle(
            Guid boardId, AppDbContext db, IBoardAccess access, ICurrentUser user, IBlobStorage storage,
            IBoardEvents events, TimeProvider clock, CancellationToken ct)
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

            var archived = await db.Boards.Where(b => b.Id == boardId).Select(b => b.ArchivedAt != null).FirstAsync(ct);
            if (!archived)
            {
                return BoardResults.Conflict("Archive the board before deleting it.");
            }

            await db.Boards.Where(b => b.Id == boardId).ExecuteDeleteAsync(ct);
            await storage.DeletePrefixAsync(AttachmentRules.BoardPrefix(boardId), ct);

            // Not an activity entry (the board's feed is gone with it), so it is published here.
            await events.PublishAsync(
                [new BoardEvent(LiveTypes.BoardDeleted, boardId, null, null, null, null, user.Id, clock.GetUtcNow())],
                null,
                ct);
            return TypedResults.NoContent();
        }
    }
}
