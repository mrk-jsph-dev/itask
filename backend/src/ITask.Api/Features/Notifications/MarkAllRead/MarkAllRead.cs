using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Notifications.MarkAllRead;

/// <summary>Marks every unread notification of the caller read.</summary>
public static class MarkAllRead
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/notifications/read-all", Handle)
                .WithTags("Notifications")
                .RequireAuthorization();

        private static async Task<NoContent> Handle(AppDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct)
        {
            var userId = user.Id;
            var now = clock.GetUtcNow();
            await db.Notifications
                .Where(n => n.UserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);
            return TypedResults.NoContent();
        }
    }
}
