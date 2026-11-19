using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Notifications.MarkRead;

/// <summary>Marks one of the caller's notifications read. Anyone else's is 404. Already read is a no-op.</summary>
public static class MarkRead
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/notifications/{id:guid}/read", Handle)
                .WithTags("Notifications")
                .RequireAuthorization();

        private static async Task<Results<NoContent, NotFound>> Handle(
            Guid id, AppDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct)
        {
            var userId = user.Id;
            var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct);
            if (notification is null)
            {
                return TypedResults.NotFound();
            }

            if (notification.ReadAt is null)
            {
                notification.ReadAt = clock.GetUtcNow();
                await db.SaveChangesAsync(ct);
            }

            return TypedResults.NoContent();
        }
    }
}
