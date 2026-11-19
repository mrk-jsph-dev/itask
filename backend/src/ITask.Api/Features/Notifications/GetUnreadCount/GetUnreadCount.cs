using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Notifications.GetUnreadCount;

/// <summary>How many unread notifications the caller has, for the bell badge (polled).</summary>
public static class GetUnreadCount
{
    public sealed record Response(int Count);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/notifications/unread-count", Handle)
                .WithTags("Notifications")
                .RequireAuthorization();

        private static async Task<Ok<Response>> Handle(AppDbContext db, ICurrentUser user, CancellationToken ct)
        {
            var userId = user.Id;
            return TypedResults.Ok(new Response(await db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null, ct)));
        }
    }
}
