using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Labels.GetLabels;

/// <summary>A board's labels by name. Any member may read.</summary>
public static class GetLabels
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/labels", Handle)
                .WithTags("Labels")
                .RequireAuthorization();

        private static async Task<Results<Ok<List<LabelChip>>, NotFound>> Handle(
            Guid boardId, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            if (await access.GetRoleAsync(boardId, ct) is null)
            {
                return TypedResults.NotFound();
            }

            var labels = await db.Labels
                .AsNoTracking()
                .Where(l => l.BoardId == boardId)
                .Select(l => new LabelChip(l.Id, l.Name, l.Color, l.Emoji))
                .ToListAsync(ct);

            return TypedResults.Ok(labels.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }
}
