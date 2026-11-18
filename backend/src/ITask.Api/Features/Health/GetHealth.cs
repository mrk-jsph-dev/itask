using ITask.Api.Common.Endpoints;

namespace ITask.Api.Features.Health;

public static class GetHealth
{
    public sealed record Response(string Status);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/health", () => TypedResults.Ok(new Response("ok")))
                .WithTags("Health")
                .AllowAnonymous()
                .DisableRateLimiting();
    }
}
