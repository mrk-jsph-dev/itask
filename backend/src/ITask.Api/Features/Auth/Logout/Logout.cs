using ITask.Api.Common.Endpoints;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Auth.Logout;

/// <summary>Clears the auth cookie.</summary>
public static class Logout
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/auth/logout", Handle)
                .WithTags("Auth")
                .RequireAuthorization();

        private static async Task<NoContent> Handle(SignInManager<AppUser> signIn)
        {
            await signIn.SignOutAsync();
            return TypedResults.NoContent();
        }
    }
}
