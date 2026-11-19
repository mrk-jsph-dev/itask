using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Invites.AcceptMyInvite;

/// <summary>
/// Accepts an invite sent to the caller's own email without its link, for the in-app
/// <c>invited</c> notification. Link-only invites and invites for other addresses are 404.
/// </summary>
public static class AcceptMyInvite
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/me/invites/{inviteId:guid}/accept", Handle)
                .WithTags("Invites")
                .RequireAuthorization();

        private static async Task<Results<Ok<InviteAcceptance.Response>, NotFound, ProblemHttpResult>> Handle(
            Guid inviteId, AppDbContext db, ICurrentUser currentUser, IActivityRecorder activity, TimeProvider clock, CancellationToken ct)
        {
            var userId = currentUser.Id;
            var email = await db.Users.Where(u => u.Id == userId).Select(u => u.NormalizedEmail).FirstAsync(ct);
            var invite = await db.BoardInvites.FirstOrDefaultAsync(i => i.Id == inviteId && i.Email != null, ct);
            if (invite is null || !string.Equals(invite.Email!.ToUpperInvariant(), email, StringComparison.Ordinal))
            {
                return TypedResults.NotFound();
            }

            return await InviteAcceptance.AcceptAsync(invite, db, userId, activity, clock.GetUtcNow(), ct);
        }
    }
}
