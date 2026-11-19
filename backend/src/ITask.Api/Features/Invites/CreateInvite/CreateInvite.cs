using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Notifications;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Email;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ITask.Api.Features.Invites.CreateInvite;

/// <summary>
/// Creates an invite to a board. Owner only. With an email only that account can accept;
/// without one anyone holding the link can. The raw token is returned only here. An email invite
/// is also emailed (best effort) and, when that address has an account, shown as a notification.
/// </summary>
public static partial class CreateInvite
{
    /// <summary>Most invite emails one person can trigger per day; later invites still work by link.</summary>
    public const int DailyEmailCap = 20;

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

    internal sealed partial class Endpoint : IEndpoint
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
            INotifier notifier,
            IAppEmailSender email,
            IOptions<EmailOptions> emailOptions,
            ILogger<Endpoint> logger,
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

            var address = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
            if (address is not null)
            {
                var normalized = address.ToUpperInvariant();
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
                Email = address,
                Role = request.Role,
                TokenHash = InviteTokens.Hash(token),
                CreatedAt = now,
                ExpiresAt = now + BoardInvite.Lifetime,
                CreatedById = user.Id,
            };
            db.BoardInvites.Add(invite);

            if (address is not null)
            {
                var normalized = address.ToUpperInvariant();
                var inviteeId = await db.Users.Where(u => u.NormalizedEmail == normalized).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
                if (inviteeId is { } id)
                {
                    notifier.Notify(NotificationTypes.Invited, [id], boardId, new { inviteId = invite.Id, role = invite.Role.ToString() });
                }
            }

            await db.SaveChangesAsync(ct);

            if (address is not null)
            {
                try
                {
                    await SendEmailAsync(db, email, emailOptions.Value, invite, token, now, ct);
                }
#pragma warning disable CA1031 // The invite exists either way; the owner can still share the link.
                catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
                {
                    LogInviteEmailFailed(logger, e);
                }
            }

            return TypedResults.Created(
                $"/api/boards/{boardId}/invites/{invite.Id}",
                new Response(invite.Id, token, invite.Email, invite.Role, invite.ExpiresAt));
        }

        /// <summary>Emails the invite unless the owner already sent <see cref="DailyEmailCap"/> in the last day (keeps the mail quota safe).</summary>
        private static async Task SendEmailAsync(
            AppDbContext db, IAppEmailSender email, EmailOptions options, BoardInvite invite, string token, DateTimeOffset now, CancellationToken ct)
        {
            var since = now.AddDays(-1);
            var sent = (await db.BoardInvites
                    .Where(i => i.CreatedById == invite.CreatedById && i.Email != null && i.Id != invite.Id)
                    .Select(i => i.CreatedAt)
                    .ToListAsync(ct))
                .Count(at => at > since);
            if (sent >= DailyEmailCap)
            {
                return;
            }

            var board = await db.Boards.Where(b => b.Id == invite.BoardId).Select(b => b.Name).FirstAsync(ct);
            var inviter = await db.Users.Where(u => u.Id == invite.CreatedById).Select(u => u.DisplayName).FirstAsync(ct);
            await email.SendAsync(
                EmailTemplates.Invite(invite.Email!, inviter, board, invite.Role.ToString(), options.Link($"invite/{token}")), ct);
        }

        [LoggerMessage(Level = LogLevel.Warning, Message = "Invite email failed; the invite link still works.")]
        private static partial void LogInviteEmailFailed(ILogger logger, Exception exception);
    }
}
