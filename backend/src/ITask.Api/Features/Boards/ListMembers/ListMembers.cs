using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.ListMembers;

/// <summary>Members of a board, Owners first, then by name. Any member can see the list.</summary>
public static class ListMembers
{
    public sealed record Item(
        Guid UserId, string DisplayName, string Email, string AvatarColor, BoardRole Role, DateTimeOffset JoinedAt);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/members", Handle)
                .WithTags("Boards")
                .RequireAuthorization();

        private static async Task<Results<Ok<List<Item>>, NotFound>> Handle(
            Guid boardId, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            if (!await access.HasAtLeastAsync(boardId, BoardRole.Viewer, ct))
            {
                return TypedResults.NotFound();
            }

            var members = await db.BoardMembers
                .AsNoTracking()
                .Where(m => m.BoardId == boardId)
                .Select(m => new Item(
                    m.UserId, m.User!.DisplayName, m.User.Email ?? string.Empty, m.User.AvatarColor, m.Role, m.JoinedAt))
                .ToListAsync(ct);

            return TypedResults.Ok(members
                .OrderByDescending(m => m.Role)
                .ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
        }
    }
}
