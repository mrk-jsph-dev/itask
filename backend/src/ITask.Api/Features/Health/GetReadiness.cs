using ITask.Api.Common.Endpoints;

namespace ITask.Api.Features.Health;

/// <summary>Readiness: the API can reach its database. Liveness stays at <c>/health</c>.</summary>
public static class GetReadiness
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapHealthChecks("/health/ready")
                .WithTags("Health")
                .AllowAnonymous()
                .DisableRateLimiting();
    }
}
