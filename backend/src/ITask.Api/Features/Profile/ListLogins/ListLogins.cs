using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Profile.ListLogins;

/// <summary>The caller's sign-in methods: password or not, connected providers, and the providers on offer.</summary>
public static class ListLogins
{
    public sealed record Login(string Provider, string? AccountName);

    public sealed record Response(bool HasPassword, IReadOnlyList<Login> Logins, IReadOnlyList<string> Providers);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/me/logins", Handle)
                .WithTags("Profile")
                .RequireAuthorization();

        private static async Task<Results<Ok<Response>, UnauthorizedHttpResult>> Handle(
            ICurrentUser currentUser, UserManager<AppUser> users, IAuthenticationSchemeProvider schemes)
        {
            var user = await users.FindByIdAsync(currentUser.Id.ToString());
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            var logins = (await users.GetLoginsAsync(user))
                .Select(l => (Provider: ExternalLogins.FindByScheme(l.LoginProvider), l.ProviderDisplayName))
                .Where(l => l.Provider is not null)
                .Select(l => new Login(l.Provider!.Id, l.ProviderDisplayName))
                .ToList();
            var providers = (await ExternalLogins.EnabledAsync(schemes)).Select(p => p.Id).ToList();

            return TypedResults.Ok(new Response(user.PasswordHash is not null, logins, providers));
        }
    }
}
