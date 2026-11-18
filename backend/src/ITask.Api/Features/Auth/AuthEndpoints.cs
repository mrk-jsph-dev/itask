using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Domain;

namespace ITask.Api.Features.Auth;

/// <summary>
/// Register, sign in (cookie), refresh, confirm email and reset password come from
/// ASP.NET Core Identity's built-in endpoints, mounted at /api/auth.
/// The Auth PR adds Turnstile (bot check) and the display-name/profile slices.
/// </summary>
internal sealed class AuthEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGroup("/auth")
            .WithTags("Auth")
            .RequireRateLimiting(RateLimits.Auth)
            .MapIdentityApi<AppUser>();
}
