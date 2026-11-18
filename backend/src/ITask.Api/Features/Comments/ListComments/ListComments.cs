using System.Globalization;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
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
    public const int DefaultLimit = 50;

    public const int MaxLimit = 100;

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
                if (!TryParse(cursor, out var createdAt, out var id))
                {
                    return TypedResults.Problem(title: "Invalid cursor.", statusCode: StatusCodes.Status400BadRequest);
                }

                query = query.Where(c => c.CreatedAt < createdAt || (c.CreatedAt == createdAt && c.Id.CompareTo(id) < 0));
            }

            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var page = await query
                .OrderByDescending(c => c.CreatedAt)
                .ThenByDescending(c => c.Id)
                .Take(take + 1)
                .Select(CommentView.Project)
                .ToListAsync(ct);

            var next = page.Count > take ? Format(page[take - 1]) : null;
            return TypedResults.Ok(new Response([.. page.Take(take)], next));
        }

        private static string Format(CommentView last) =>
            $"{last.CreatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}_{last.Id:N}";

        private static bool TryParse(string cursor, out DateTimeOffset createdAt, out Guid id)
        {
            createdAt = default;
            id = default;
            var parts = cursor.Split('_');
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks > DateTimeOffset.MaxValue.UtcTicks
                || !Guid.TryParseExact(parts[1], "N", out id))
            {
                return false;
            }

            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
    }
}
