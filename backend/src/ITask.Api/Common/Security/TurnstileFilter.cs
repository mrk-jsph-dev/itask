using ITask.Api.Infrastructure.Turnstile;

namespace ITask.Api.Common.Security;

/// <summary>
/// Rejects the request with 400 unless the <c>X-Turnstile-Token</c> header holds a valid
/// Cloudflare Turnstile token. A header is used so the filter also works on endpoints
/// whose body shape is owned by ASP.NET Core Identity.
/// </summary>
public sealed class TurnstileFilter : IEndpointFilter
{
    public const string Header = "X-Turnstile-Token";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        var verifier = http.RequestServices.GetRequiredService<ITurnstileVerifier>();
        var token = http.Request.Headers[Header].ToString();

        if (!await verifier.VerifyAsync(token, http.Connection.RemoteIpAddress, http.RequestAborted))
        {
            return TypedResults.Problem(
                title: "Human verification failed.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        return await next(context);
    }
}

public static class TurnstileExtensions
{
    public static TBuilder RequireTurnstile<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter<TBuilder, TurnstileFilter>();
}
