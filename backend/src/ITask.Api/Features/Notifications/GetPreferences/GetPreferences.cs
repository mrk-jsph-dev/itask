using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Notifications;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Notifications.GetPreferences;

/// <summary>The caller's email digest settings; defaults when never saved.</summary>
public static class GetPreferences
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/me/notification-preferences", Handle)
                .WithTags("Notifications")
                .RequireAuthorization();

        private static async Task<Ok<NotificationPreferencesView>> Handle(AppDbContext db, ICurrentUser user, CancellationToken ct)
        {
            var userId = user.Id;
            var prefs = await db.NotificationPreferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct)
                ?? new NotificationPreference { UserId = userId };
            var emailConfirmed = await db.Users.Where(u => u.Id == userId).Select(u => u.EmailConfirmed).FirstAsync(ct);
            return TypedResults.Ok(NotificationPreferencesView.From(prefs, emailConfirmed));
        }
    }
}
