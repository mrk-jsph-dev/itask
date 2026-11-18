namespace ITask.Api.Common.Security;

/// <summary>
/// Rejects state-changing requests sent from a foreign origin. With a <c>SameSite=None</c> cookie the
/// browser attaches it to cross-site form posts too, and CORS doesn't stop those (no preflight).
/// Requests without an <c>Origin</c> header (non-browser clients) pass.
/// </summary>
public static class OriginGuard
{
    public static IApplicationBuilder UseOriginGuard(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var config = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var allowed = new HashSet<string>(
            config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [], StringComparer.OrdinalIgnoreCase);

        return app.Use(async (context, next) =>
        {
            var request = context.Request;
            if (!HttpMethods.IsGet(request.Method)
                && !HttpMethods.IsHead(request.Method)
                && !HttpMethods.IsOptions(request.Method)
                && request.Headers.Origin is [{ Length: > 0 } origin]
                && !allowed.Contains(origin)
                && !string.Equals(origin, $"{request.Scheme}://{request.Host}", StringComparison.OrdinalIgnoreCase))
            {
                await TypedResults.Problem(title: "Origin not allowed.", statusCode: StatusCodes.Status403Forbidden)
                    .ExecuteAsync(context);
                return;
            }

            await next();
        });
    }
}
