using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Invites.ListInvites;

/// <summary>Pending (not used, not expired) invites of a board, newest first. Owner only.</summary>
public static class ListInvites
{
    public sealed record Item(Guid Id, string? Email, BoardRole Role, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/invites", Handle)
                .WithTags("Invites")
                .RequireAuthorization();

        private static async Task<Results<Ok<List<Item>>, NotFound, ProblemHttpResult>> Handle(
            Guid boardId, AppDbContext db, IBoardAccess access, TimeProvider clock, CancellationToken ct)
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

            // Expiry is filtered in memory: SQLite can't compare DateTimeOffset columns.
            var now = clock.GetUtcNow();
            var invites = await db.BoardInvites
                .AsNoTracking()
                .Where(i => i.BoardId == boardId && i.AcceptedAt == null)
                .Select(i => new Item(i.Id, i.Email, i.Role, i.CreatedAt, i.ExpiresAt))
                .ToListAsync(ct);

            return TypedResults.Ok(invites
                .Where(i => i.ExpiresAt > now)
                .OrderByDescending(i => i.CreatedAt)
                .ToList());
        }
    }
}
