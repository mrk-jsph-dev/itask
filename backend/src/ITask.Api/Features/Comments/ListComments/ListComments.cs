using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Paging;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Comments.ListComments;

/// <summary>
/// A card's comments, newest first. Any member may read. <c>cursor</c> is the <c>nextCursor</c> of
/// the previous page: the last comment's creation time and id, so pages stay stable while
/// comments are added.
/// </summary>
public static class ListComments
{
    public sealed record Response(IReadOnlyList<CommentView> Items, string? NextCursor);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/cards/{cardId:guid}/comments", Handle)
                .WithTags("Comments")
                .RequireAuthorization();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, string? cursor, int? limit, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            if (await access.GetRoleAsync(boardId, ct) is null || !await CardGuard.CardExistsAsync(db, boardId, cardId, ct))
            {
                return CardGuard.NotFound();
            }

            var query = db.Comments.AsNoTracking().Where(c => c.CardId == cardId);
            if (cursor is not null)
            {
                if (!TimeCursor.TryParse(cursor, out var createdAt, out var id))
                {
                    return TypedResults.Problem(title: "Invalid cursor.", statusCode: StatusCodes.Status400BadRequest);
                }

                query = query.Where(c => c.CreatedAt < createdAt || (c.CreatedAt == createdAt && c.Id.CompareTo(id) < 0));
            }

            var take = TimeCursor.Take(limit);
            var page = await query
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .Take(take + 1)
                .Select(CommentView.Project)
                .ToListAsync(ct);

            var next = page.Count > take ? TimeCursor.Format(page[take - 1].CreatedAt, page[take - 1].Id) : null;
            return TypedResults.Ok(new Response([.. page.Take(take)], next));
        }
    }
}
