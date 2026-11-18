using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.GetMyBoards;

/// <summary>
/// Boards the caller is a member of. Archived boards are listed only with <c>archived=true</c>;
/// <c>starred=true</c> limits the list to the caller's starred boards.
/// </summary>
public static class GetMyBoards
{
    public sealed record Item(
        Guid Id,
        string Name,
        string? Emoji,
        string? Description,
        string Color,
        BoardRole Role,
        bool IsStarred,
        int MemberCount,
        DateTimeOffset? ArchivedAt);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards", Handle)
                .WithTags("Boards")
                .RequireAuthorization();

        private static async Task<Ok<List<Item>>> Handle(
            AppDbContext db, ICurrentUser user, CancellationToken ct, bool? starred = null, bool? archived = null)
        {
            var userId = user.Id;
            var query = db.BoardMembers.AsNoTracking().Where(m => m.UserId == userId);

            query = archived == true
                ? query.Where(m => m.Board!.ArchivedAt != null)
                : query.Where(m => m.Board!.ArchivedAt == null);

            if (starred == true)
            {
                query = query.Where(m => m.Board!.Stars.Any(s => s.UserId == userId));
            }

            var boards = await query
                .OrderBy(m => m.Board!.Name)
                .Select(m => new Item(
                    m.BoardId,
                    m.Board!.Name,
                    m.Board.Emoji,
                    m.Board.Description,
                    m.Board.Color,
                    m.Role,
                    m.Board.Stars.Any(s => s.UserId == userId),
                    m.Board.Members.Count,
                    m.Board.ArchivedAt))
                .ToListAsync(ct);

            return TypedResults.Ok(boards);
        }
    }
}
