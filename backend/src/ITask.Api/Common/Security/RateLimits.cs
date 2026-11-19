using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace ITask.Api.Common.Security;

/// <summary>
/// Fixed-window limits partitioned per user id when signed in, otherwise per client IP.
/// The <see cref="Auth"/> policy applies a tighter limit to sign-in and registration,
/// <see cref="Search"/> to search-as-you-type, <see cref="Ai"/> to AI calls (on top of the daily cap).
/// </summary>
public static class RateLimits
{
    public const string Auth = "auth";

    public const string Search = "search";

    public const string Ai = "ai";

    public static IServiceCollection AddITaskRateLimits(this IServiceCollection services, IConfiguration config)
    {
        services.AddRateLimiter(options =>
        {
            // Read here, not at registration, so test host overrides apply.
            var perMinute = config.GetValue("RateLimits:PerMinute", 120);
            var authPerMinute = config.GetValue("RateLimits:AuthPerMinute", 10);
            var searchPerMinute = config.GetValue("RateLimits:SearchPerMinute", 60);
            var aiPerMinute = config.GetValue("RateLimits:AiPerMinute", 10);

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

            options.AddPolicy(Search, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = searchPerMinute,
                    Window = TimeSpan.FromMinutes(1),
                }));

            options.AddPolicy(Ai, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = aiPerMinute,
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
