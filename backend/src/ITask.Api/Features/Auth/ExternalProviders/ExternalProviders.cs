using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Auth.ExternalProviders;

/// <summary>Lists the external sign-in providers that are configured, so the SPA knows which buttons to show.</summary>
public static class ExternalProviders
{
    public sealed record Response(IReadOnlyList<string> Providers);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/auth/providers", Handle)
                .WithTags("Auth");

        private static async Task<Ok<Response>> Handle(IAuthenticationSchemeProvider schemes) =>
            TypedResults.Ok(new Response([.. (await ExternalLogins.EnabledAsync(schemes)).Select(p => p.Id)]));
    }
}
