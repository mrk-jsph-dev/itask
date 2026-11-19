using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Profile.RemoveLogin;

/// <summary>Disconnects a provider, unless it is the caller's only way to sign in (409).</summary>
public static class RemoveLogin
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/me/logins/{provider}", Handle)
                .WithTags("Profile")
                .RequireAuthorization()
                .RequireNonDemo();

        private static async Task<Results<NoContent, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> Handle(
            string provider, ICurrentUser currentUser, UserManager<AppUser> users, SignInManager<AppUser> signIn)
        {
            var user = await users.FindByIdAsync(currentUser.Id.ToString());
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            var logins = await users.GetLoginsAsync(user);
            var scheme = ExternalLogins.FindById(provider)?.Scheme;
            var login = logins.FirstOrDefault(l => l.LoginProvider == scheme);
            if (login is null)
            {
                return TypedResults.NotFound();
            }

            if (user.PasswordHash is null && logins.Count == 1)
            {
                return TypedResults.Problem(
                    title: "Set a password or connect another account before disconnecting this one.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var result = await users.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
            if (!result.Succeeded)
            {
                return TypedResults.Problem(title: "Could not disconnect.", statusCode: StatusCodes.Status409Conflict);
            }

            await signIn.RefreshSignInAsync(user);
            return TypedResults.NoContent();
        }
    }
}
