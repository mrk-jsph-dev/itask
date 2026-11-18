using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Domain;

namespace ITask.Api.Features.Auth;

/// <summary>
/// Mounts ASP.NET Core Identity's endpoints (login, refresh, confirm email, password reset)
/// at /api/auth with the auth rate-limit policy. Login and forgot-password also require a
/// Turnstile token. Register is replaced by the Register slice.
/// </summary>
internal sealed class AuthEndpoints : IEndpoint
{
    private static readonly string[] TurnstilePaths = ["/api/auth/login", "/api/auth/forgotPassword"];

    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth")
            .WithTags("Auth")
            .RequireRateLimiting(RateLimits.Auth);

        var turnstile = new TurnstileFilter();
        group.AddEndpointFilter((context, next) =>
            TurnstilePaths.Contains(context.HttpContext.Request.Path.Value, StringComparer.OrdinalIgnoreCase)
                ? turnstile.InvokeAsync(context, next)
                : next(context));

        group.MapIdentityApi<AppUser>();
    }
}
