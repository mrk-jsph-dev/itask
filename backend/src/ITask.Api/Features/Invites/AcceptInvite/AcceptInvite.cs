using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Invites.AcceptInvite;

/// <summary>
/// Joins the board an invite link points to. Unknown or revoked tokens are 404; the rest follows
/// <see cref="InviteAcceptance"/>.
/// </summary>
public static class AcceptInvite
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/invites/{token}/accept", Handle)
                .WithTags("Invites")
                .RequireAuthorization();

        private static async Task<Results<Ok<InviteAcceptance.Response>, NotFound, ProblemHttpResult>> Handle(
            string token, AppDbContext db, ICurrentUser currentUser, IActivityRecorder activity, TimeProvider clock, CancellationToken ct)
        {
            var hash = InviteTokens.Hash(token);
            var invite = await db.BoardInvites.FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
            if (invite is null)
            {
                return TypedResults.NotFound();
            }

            return await InviteAcceptance.AcceptAsync(invite, db, currentUser.Id, activity, clock.GetUtcNow(), ct);
        }
    }
}
