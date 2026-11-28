using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Profile.GetMe;

public static class GetMe
{
    public sealed record Response(
        Guid Id,
        string Email,
        string DisplayName,
        string AvatarColor,
        string TimeZone,
        ThemePreference ThemePreference,
        bool IsPlatformAdmin,
        bool IsDemo,
        bool HasPassword,
        DateTimeOffset CreatedAt);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/me", Handle)
                .WithTags("Profile")
                .RequireAuthorization();

        private static async Task<Results<Ok<Response>, UnauthorizedHttpResult>> Handle(
            ICurrentUser currentUser, AppDbContext db, UserManager<AppUser> users, CancellationToken ct)
        {
            var user = await db.Users.FindAsync([currentUser.Id], ct);
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            var isAdmin = await users.IsInRoleAsync(user, PlatformRoles.PlatformAdmin);
            return TypedResults.Ok(new Response(
                user.Id,
                user.Email ?? string.Empty,
                user.DisplayName,
                user.AvatarColor,
                user.TimeZone,
                user.ThemePreference,
                isAdmin,
                user.IsDemo,
                user.PasswordHash is not null,
                user.CreatedAt));
        }
    }
}
