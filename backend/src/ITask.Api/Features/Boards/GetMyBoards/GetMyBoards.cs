using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.GetMyBoards;

public static class GetMyBoards
{
    public sealed record Item(Guid Id, string Name, string? Emoji, BoardRole Role);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards", Handle)
                .WithTags("Boards")
                .RequireAuthorization();

        private static async Task<Ok<List<Item>>> Handle(AppDbContext db, ICurrentUser user, CancellationToken ct)
        {
            var userId = user.Id;
            var boards = await db.BoardMembers
                .AsNoTracking()
                .Where(m => m.UserId == userId)
                .OrderBy(m => m.Board!.Name)
                .Select(m => new Item(m.BoardId, m.Board!.Name, m.Board.Emoji, m.Role))
                .ToListAsync(ct);

            return TypedResults.Ok(boards);
        }
    }
}
