using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Notifications;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Notifications.UpdatePreferences;

/// <summary>
/// Saves the caller's email digest settings. Turning the digest on starts from now, so the first
/// digest doesn't replay older notifications.
/// </summary>
public static class UpdatePreferences
{
    public sealed record Request(DigestFrequency Digest, DigestTypes Types);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Digest).IsInEnum();
            RuleFor(r => r.Types).NotNull();
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/me/notification-preferences", Handle)
                .WithTags("Notifications")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<NotificationPreferencesView>, ValidationProblem>> Handle(
            Request request, AppDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken ct)
        {
            var userId = user.Id;
            var prefs = await db.NotificationPreferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
            if (prefs is null)
            {
                prefs = new NotificationPreference { UserId = userId };
                db.NotificationPreferences.Add(prefs);
            }

            if (prefs.Digest == DigestFrequency.Off && request.Digest != DigestFrequency.Off)
            {
                prefs.LastDigestAt = clock.GetUtcNow();
            }

            prefs.Digest = request.Digest;
            prefs.Mentioned = request.Types.Mentioned;
            prefs.Assigned = request.Types.Assigned;
            prefs.DueSoon = request.Types.DueSoon;
            prefs.Overdue = request.Types.Overdue;
            prefs.Invited = request.Types.Invited;
            prefs.CommentOnMyCard = request.Types.CommentOnMyCard;
            await db.SaveChangesAsync(ct);

            var emailConfirmed = await db.Users.Where(u => u.Id == userId).Select(u => u.EmailConfirmed).FirstAsync(ct);
            return TypedResults.Ok(NotificationPreferencesView.From(prefs, emailConfirmed));
        }
    }
}
