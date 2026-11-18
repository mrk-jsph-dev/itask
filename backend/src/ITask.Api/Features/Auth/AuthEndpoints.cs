using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Domain;

namespace ITask.Api.Features.Auth;

/// <summary>
/// Mounts ASP.NET Core Identity's endpoints (register, login, refresh, confirm email,
/// password reset) at /api/auth with the auth rate-limit policy.
/// </summary>
internal sealed class AuthEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder app) =>
        app.MapGroup("/auth")
            .WithTags("Auth")
            .RequireRateLimiting(RateLimits.Auth)
            .MapIdentityApi<AppUser>();
}
