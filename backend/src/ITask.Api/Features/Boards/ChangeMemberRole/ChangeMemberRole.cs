using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.ChangeMemberRole;

/// <summary>Changes a member's role. Owner only; a board always keeps at least one Owner.</summary>
public static class ChangeMemberRole
{
    public sealed record Request(BoardRole Role);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.Role).IsInEnum();
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/members/{userId:guid}", Handle)
                .WithTags("Boards")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Handle(
            Guid boardId, Guid userId, Request request, AppDbContext db, IBoardAccess access, CancellationToken ct)
        {
            var role = await access.GetRoleAsync(boardId, ct);
            if (role is null)
            {
                return TypedResults.NotFound();
            }

            if (role < BoardRole.Owner)
            {
                return BoardResults.Forbidden();
            }

            var member = await db.BoardMembers.FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == userId, ct);
            if (member is null)
            {
                return TypedResults.NotFound();
            }

            if (member.Role == BoardRole.Owner && request.Role != BoardRole.Owner
                && await db.BoardMembers.CountAsync(m => m.BoardId == boardId && m.Role == BoardRole.Owner, ct) == 1)
            {
                return BoardResults.Conflict("A board needs at least one Owner.");
            }

            member.Role = request.Role;
            await db.SaveChangesAsync(ct);
            // activity: member role changed

            return TypedResults.NoContent();
        }
    }
}
