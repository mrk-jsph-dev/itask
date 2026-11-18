using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Lists.GetBoardLists;

/// <summary>
/// A board's lists in order with their active cards. Any member may read; <c>?archived=true</c>
/// returns the archived lists instead, newest archived first and without cards.
/// </summary>
public static class GetBoardLists
{
    public sealed record Item(
        Guid Id,
        string Name,
        string? Emoji,
        double Position,
        int? WipLimit,
        DateTimeOffset? ArchivedAt,
        IReadOnlyList<CardSummary> Cards);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/lists", Handle)
                .WithTags("Lists")
                .RequireAuthorization();

        private static async Task<Results<Ok<List<Item>>, NotFound>> Handle(
            Guid boardId, AppDbContext db, IBoardAccess access, CancellationToken ct, bool archived = false)
        {
            if (await access.GetRoleAsync(boardId, ct) is null)
            {
                return TypedResults.NotFound();
            }

            var query = db.Lists.AsNoTracking().Where(l => l.BoardId == boardId);
            var lists = archived
                ? await query.Where(l => l.ArchivedAt != null).ToListAsync(ct)
                : await query.Where(l => l.ArchivedAt == null).OrderBy(l => l.Position).ToListAsync(ct);

            // SQLite cannot order by DateTimeOffset, so archived lists are sorted in memory.
            if (archived)
            {
                lists = [.. lists.OrderByDescending(l => l.ArchivedAt)];
            }

            var cards = archived
                ? []
                : await CardSummaries.QueryAsync(db, boardId, c => c.ArchivedAt == null, ct);
            var byList = cards.ToLookup(c => c.ListId);

            return TypedResults.Ok(lists
                .Select(l => new Item(l.Id, l.Name, l.Emoji, l.Position, l.WipLimit, l.ArchivedAt, [.. byList[l.Id]]))
                .ToList());
        }
    }
}
