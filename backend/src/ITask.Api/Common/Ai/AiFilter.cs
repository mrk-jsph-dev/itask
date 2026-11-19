using ITask.Api.Common.Security;
using ITask.Api.Infrastructure.Ai;

namespace ITask.Api.Common.Ai;

/// <summary>404 when AI is off in this environment.</summary>
public sealed class AiEnabledFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        return context.HttpContext.RequestServices.GetRequiredService<IAiService>().IsEnabled
            ? await next(context)
            : AiResults.Disabled();
    }
}

public static class AiEndpointExtensions
{
    /// <summary>AI endpoint conventions: signed in, AI enabled (404), not a demo account (403), AI rate limit.</summary>
    public static TBuilder RequireAi<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder
            .RequireAuthorization()
            .AddEndpointFilter<TBuilder, AiEnabledFilter>()
            .RequireNonDemo()
            .RequireRateLimiting(RateLimits.Ai)
            .WithTags("AI");
}
