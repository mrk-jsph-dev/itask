using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Profile.GetMyStats;

/// <summary>
/// Counts for the profile card: active boards the caller belongs to, active cards assigned to
/// the caller on those boards, and how many of those are complete.
/// </summary>
public static class GetMyStats
{
    public sealed record Response(int Boards, int Tasks, int Done);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/me/stats", Handle)
                .WithTags("Profile")
                .RequireAuthorization();

        private static async Task<Ok<Response>> Handle(ICurrentUser user, AppDbContext db, CancellationToken ct)
        {
            var userId = user.Id;
            var boards = await db.BoardMembers
                .CountAsync(m => m.UserId == userId && m.Board!.ArchivedAt == null, ct);

            var assigned = db.Cards.Where(c =>
                c.ArchivedAt == null
                && c.List!.ArchivedAt == null
                && c.Board!.ArchivedAt == null
                && c.Assignees.Any(a => a.UserId == userId)
                && db.BoardMembers.Any(m => m.BoardId == c.BoardId && m.UserId == userId));
            var tasks = await assigned.CountAsync(ct);
            var done = await assigned.CountAsync(c => c.CompletedAt != null, ct);

            return TypedResults.Ok(new Response(boards, tasks, done));
        }
    }
}
