using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Demo;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ITask.Api.Features.Auth.Demo;

/// <summary>
/// Signs in as a seeded demo account without a password. 404 unless demo mode is on,
/// the role is known and the account exists.
/// </summary>
public static class DemoSignIn
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/auth/demo/{role}", Handle)
                .WithTags("Auth")
                .RequireRateLimiting(RateLimits.Auth);

        private static async Task<Results<NoContent, NotFound>> Handle(
            string role,
            IOptions<DemoOptions> options,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn)
        {
            if (!options.Value.IsActive || DemoAccounts.Find(role) is not { } account)
            {
                return TypedResults.NotFound();
            }

            var user = await users.FindByEmailAsync(account.Email);
            if (user is not { IsDemo: true })
            {
                return TypedResults.NotFound();
            }

            await signIn.SignInAsync(user, isPersistent: false);
            return TypedResults.NoContent();
        }
    }
}
