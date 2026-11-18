using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Invites.AcceptInvite;

/// <summary>
/// Joins the board an invite points to. Unknown or revoked tokens are 404, used or expired
/// ones 410, and an email invite opened by another account 403. An existing member keeps
/// their role; the invite is still used up.
/// </summary>
public static class AcceptInvite
{
    public sealed record Response(Guid BoardId);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/invites/{token}/accept", Handle)
                .WithTags("Invites")
                .RequireAuthorization();

        private static async Task<Results<Ok<Response>, NotFound, ProblemHttpResult>> Handle(
            string token, AppDbContext db, ICurrentUser currentUser, TimeProvider clock, CancellationToken ct)
        {
            var hash = InviteTokens.Hash(token);
            var invite = await db.BoardInvites.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
            if (invite is null)
            {
                return TypedResults.NotFound();
            }

            var now = clock.GetUtcNow();
            if (invite.AcceptedAt is not null || invite.ExpiresAt <= now)
            {
                return Gone();
            }

            var user = await db.Users.FirstAsync(u => u.Id == currentUser.Id, ct);
            if (invite.Email is not null && !string.Equals(invite.Email, user.Email, StringComparison.OrdinalIgnoreCase))
            {
                return TypedResults.Problem(
                    title: "This invite was sent to a different email address.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var isMember = await db.BoardMembers.AnyAsync(m => m.BoardId == invite.BoardId && m.UserId == user.Id, ct);
            if (!isMember)
            {
                db.BoardMembers.Add(new BoardMember { BoardId = invite.BoardId, UserId = user.Id, Role = invite.Role, JoinedAt = now });
            }

            invite.AcceptedAt = now;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Gone();
            }

            // activity: member joined

            return TypedResults.Ok(new Response(invite.BoardId));
        }

        private static ProblemHttpResult Gone() => TypedResults.Problem(
            title: "This invite has expired or was already used.",
            statusCode: StatusCodes.Status410Gone);
    }
}
