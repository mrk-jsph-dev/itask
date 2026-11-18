using ITask.Api.Common.Auth;
using ITask.Api.Common.Persistence;

namespace ITask.Api.Common.Security;

/// <summary>Returns 403 when the signed-in user is a seeded demo account.</summary>
public sealed class DemoGuard : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var services = context.HttpContext.RequestServices;
        var currentUser = services.GetRequiredService<ICurrentUser>();
        if (currentUser.IsAuthenticated)
        {
            var db = services.GetRequiredService<AppDbContext>();
            var user = await db.Users.FindAsync([currentUser.Id], context.HttpContext.RequestAborted);
            if (user?.IsDemo == true)
            {
                return TypedResults.Problem(
                    title: "Not available for demo accounts.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        return await next(context);
    }
}

public static class DemoGuardExtensions
{
    public static TBuilder RequireNonDemo<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
        => builder.AddEndpointFilter<TBuilder, DemoGuard>();
}
