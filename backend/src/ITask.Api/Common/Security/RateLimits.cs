using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace ITask.Api.Common.Security;

/// <summary>
/// Fixed-window limits partitioned per user id when signed in, otherwise per client IP.
/// The <see cref="Auth"/> policy applies a tighter limit to sign-in and registration.
/// </summary>
public static class RateLimits
{
    public const string Auth = "auth";

    public static IServiceCollection AddITaskRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var perMinute = config.GetValue("RateLimits:PerMinute", 120);
        var authPerMinute = config.GetValue("RateLimits:AuthPerMinute", 10);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));

            options.AddPolicy(Auth, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = authPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));
        });

        return services;
    }

    private static string PartitionKey(HttpContext context)
        => context.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? context.Connection.RemoteIpAddress?.ToString()
           ?? string.Create(CultureInfo.InvariantCulture, $"unknown");
}
