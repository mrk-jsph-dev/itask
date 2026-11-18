using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Activity.GetMyActivity;

/// <summary>
/// The caller's own changes across boards they are still a member of, newest first.
/// Leaving a board hides its entries here too.
/// </summary>
public static class GetMyActivity
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/me/activity", Handle)
                .WithTags("Activity")
                .RequireAuthorization();

        private static async Task<Results<Ok<ActivityPage>, ProblemHttpResult>> Handle(
            string? type, string? cursor, int? limit, AppDbContext db, ICurrentUser user, CancellationToken ct)
        {
            if (ActivityFeed.Validate(type, cursor) is { } invalid)
            {
                return invalid;
            }

            var userId = user.Id;
            var query = db.Activity.Where(a =>
                a.ActorId == userId && db.BoardMembers.Any(m => m.BoardId == a.BoardId && m.UserId == userId));
            return TypedResults.Ok(await ActivityFeed.PageAsync(query, type, cursor, limit, ct));
        }
    }
}
