using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Activity.GetCardActivity;

/// <summary>One card's activity, newest first. Any member may read.</summary>
public static class GetCardActivity
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/cards/{cardId:guid}/activity", Handle)
                .WithTags("Activity")
                .RequireAuthorization();

        private static async Task<Results<Ok<ActivityPage>, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, string? cursor, int? limit, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            if (await access.GetRoleAsync(boardId, ct) is null || !await CardGuard.CardExistsAsync(db, boardId, cardId, ct))
            {
                return CardGuard.NotFound();
            }

            if (ActivityFeed.Validate(null, cursor) is { } invalid)
            {
                return invalid;
            }

            var query = db.Activity.Where(a => a.BoardId == boardId && a.CardId == cardId);
            return TypedResults.Ok(await ActivityFeed.PageAsync(query, null, cursor, limit, ct));
        }
    }
}
