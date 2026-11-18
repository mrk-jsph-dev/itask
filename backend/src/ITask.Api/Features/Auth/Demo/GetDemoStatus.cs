using ITask.Api.Common.Endpoints;
using ITask.Api.Infrastructure.Demo;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace ITask.Api.Features.Auth.Demo;

/// <summary>Tells the sign-in page whether to offer the demo sign-in buttons.</summary>
public static class GetDemoStatus
{
    public sealed record Response(bool Enabled);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/auth/demo", Handle)
                .WithTags("Auth");

        private static Ok<Response> Handle(IOptions<DemoOptions> options)
            => TypedResults.Ok(new Response(options.Value.IsActive));
    }
}
