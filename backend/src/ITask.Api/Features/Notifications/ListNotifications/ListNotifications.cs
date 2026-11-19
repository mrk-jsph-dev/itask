using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Notifications;
using ITask.Api.Common.Paging;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Notifications.ListNotifications;

/// <summary>The caller's notifications, newest first; <c>unread=true</c> leaves out read ones.</summary>
public static class ListNotifications
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/notifications", Handle)
                .WithTags("Notifications")
                .RequireAuthorization();

        private static async Task<Results<Ok<NotificationPage>, ProblemHttpResult>> Handle(
            bool? unread, string? cursor, int? limit, AppDbContext db, ICurrentUser user, CancellationToken ct)
        {
            if (cursor is not null && !TimeCursor.TryParse(cursor, out _, out _))
            {
                return TypedResults.Problem(title: "Invalid cursor.", statusCode: StatusCodes.Status400BadRequest);
            }

            var userId = user.Id;
            var query = db.Notifications.Where(n => n.UserId == userId);
            if (unread == true)
            {
                query = query.Where(n => n.ReadAt == null);
            }

            return TypedResults.Ok(await NotificationFeed.PageAsync(query, cursor, limit, ct));
        }
    }
}
