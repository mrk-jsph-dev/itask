using ITask.Api.Common.Ai;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Ai.GetAiStatus;

/// <summary>Whether AI can be used by the caller and how many requests are left today. Demo accounts see it disabled.</summary>
public static class GetAiStatus
{
    public sealed record Response(bool Enabled, int Limit, int Remaining);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/ai/status", Handle)
                .WithTags("AI")
                .RequireAuthorization();

        private static async Task<Ok<Response>> Handle(
            IAiService ai, AiGateway gateway, AppDbContext db, ICurrentUser user, CancellationToken ct)
        {
            var enabled = ai.IsEnabled
                && !await db.Users.AnyAsync(u => u.Id == user.Id && u.IsDemo, ct);
            return TypedResults.Ok(enabled
                ? new Response(true, gateway.DailyLimit, await gateway.RemainingAsync(ct))
                : new Response(false, 0, 0));
        }
    }
}
