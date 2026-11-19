using ITask.Api.Common.Activity;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Auth;

/// <summary>
/// Accepting an invite, shared by the token link and the in-app notification. Used or expired
/// invites are 410 and an email invite opened by another account 403. An existing member keeps
/// their role; the invite is still used up.
/// </summary>
public static class InviteAcceptance
{
    public sealed record Response(Guid BoardId);

    public static async Task<Results<Ok<Response>, NotFound, ProblemHttpResult>> AcceptAsync(
        BoardInvite invite, AppDbContext db, Guid userId, IActivityRecorder activity, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invite);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(activity);

        if (invite.AcceptedAt is not null || invite.ExpiresAt <= now)
        {
            return Gone();
        }

        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
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
            activity.Record(ActivityTypes.MemberJoined, invite.BoardId, null, new
            {
                member = new { id = user.Id, name = user.DisplayName },
                role = invite.Role.ToString(),
            });
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

        return TypedResults.Ok(new Response(invite.BoardId));
    }

    private static ProblemHttpResult Gone() => TypedResults.Problem(
        title: "This invite has expired or was already used.",
        statusCode: StatusCodes.Status410Gone);
}
