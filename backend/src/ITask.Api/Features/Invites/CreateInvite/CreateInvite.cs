using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Invites.CreateInvite;

/// <summary>
/// Creates an invite to a board. Owner only. With an email only that account can accept;
/// without one anyone holding the link can. The raw token is returned only here.
/// </summary>
public static class CreateInvite
{
    public sealed record Request(string? Email, BoardRole Role);

    public sealed record Response(Guid Id, string Token, string? Email, BoardRole Role, DateTimeOffset ExpiresAt);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Email).EmailAddress().MaximumLength(256).When(r => !string.IsNullOrWhiteSpace(r.Email));
            RuleFor(r => r.Role).IsInEnum();
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/invites", Handle)
                .WithTags("Invites")
                .RequireAuthorization()
                .RequireNonDemo()
                .WithValidation<Request>();

        private static async Task<Results<Created<Response>, NotFound, ProblemHttpResult>> Handle(
            Guid boardId,
            Request request,
            AppDbContext db,
            IBoardAccess access,
            ICurrentUser user,
            TimeProvider clock,
            CancellationToken ct)
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

            var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            if (email is not null)
            {
                var normalized = email.ToUpperInvariant();
                var alreadyMember = await db.BoardMembers
                    .AnyAsync(m => m.BoardId == boardId && m.User!.NormalizedEmail == normalized, ct);
                if (alreadyMember)
                {
                    return BoardResults.Conflict("That person is already a member of this board.");
                }
            }

            var token = InviteTokens.Create();
            var now = clock.GetUtcNow();
            var invite = new BoardInvite
            {
                BoardId = boardId,
                Email = email,
                Role = request.Role,
                TokenHash = InviteTokens.Hash(token),
                CreatedAt = now,
                ExpiresAt = now + BoardInvite.Lifetime,
                CreatedById = user.Id,
            };
            db.BoardInvites.Add(invite);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created(
                $"/api/boards/{boardId}/invites/{invite.Id}",
                new Response(invite.Id, token, invite.Email, invite.Role, invite.ExpiresAt));
        }
    }
}
