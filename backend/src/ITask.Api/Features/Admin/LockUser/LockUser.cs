using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Admin.LockUser;

/// <summary>
/// Locks or unlocks an account. Locking also rotates the security stamp so the user's
/// cookie stops working at the next stamp check (within a minute). Admins can't lock themselves.
/// </summary>
public static class LockUser
{
    public sealed record Request(bool Locked);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/admin/users/{userId:guid}/lock", Handle)
                .WithTags("Admin")
                .RequireAuthorization(PlatformRoles.PlatformAdmin)
                .RequireNonDemo();

        private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Handle(
            Guid userId, Request request, UserManager<AppUser> users, ICurrentUser currentUser)
        {
            if (userId == currentUser.Id)
            {
                return TypedResults.Problem(title: "You can't lock your own account.", statusCode: StatusCodes.Status409Conflict);
            }

            var user = await users.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                return TypedResults.NotFound();
            }

            if (request.Locked)
            {
                await users.SetLockoutEnabledAsync(user, true);
                await users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                await users.UpdateSecurityStampAsync(user);
            }
            else
            {
                await users.SetLockoutEndDateAsync(user, null);
                await users.ResetAccessFailedCountAsync(user);
            }

            return TypedResults.NoContent();
        }
    }
}
