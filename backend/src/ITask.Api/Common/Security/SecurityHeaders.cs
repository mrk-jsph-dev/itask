namespace ITask.Api.Common.Security;

public static class SecurityHeaders
{
    /// <summary>Conservative headers for a JSON-only API.</summary>
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers["Cross-Origin-Resource-Policy"] = "same-site";
            await next();
        });
}
