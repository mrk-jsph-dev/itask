using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Cards.GetCard;

/// <summary>One card with its description, including archived cards. Any member may read.</summary>
public static class GetCard
{
    public sealed record Response(
        CardSummary Card,
        string? Description,
        Guid CreatedById,
        string? CreatedByName,
        DateTimeOffset CreatedAt);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/cards/{cardId:guid}", Handle)
                .WithTags("Cards")
                .RequireAuthorization();

        private static async Task<Results<Ok<Response>, NotFound>> Handle(
            Guid boardId, Guid cardId, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            if (await access.GetRoleAsync(boardId, ct) is null)
            {
                return TypedResults.NotFound();
            }

            var summary = await CardSummaries.FindAsync(db, boardId, cardId, ct);
            if (summary is null)
            {
                return TypedResults.NotFound();
            }

            var detail = await db.Cards
                .AsNoTracking()
                .Where(c => c.Id == cardId)
                .Select(c => new
                {
                    c.Description,
                    c.CreatedById,
                    c.CreatedAt,
                    CreatedByName = db.Users.Where(u => u.Id == c.CreatedById).Select(u => u.DisplayName).FirstOrDefault(),
                })
                .FirstAsync(ct);

            return TypedResults.Ok(new Response(
                summary, detail.Description, detail.CreatedById, detail.CreatedByName, detail.CreatedAt));
        }
    }
}
