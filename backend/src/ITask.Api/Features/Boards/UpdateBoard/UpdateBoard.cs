using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Boards.UpdateBoard;

/// <summary>
/// Changes a board's details. Owner only. <c>RowVersion</c> must match the stored one,
/// otherwise someone else saved first and the response is 409. <c>KeyPrefix</c> is optional;
/// changing it changes every card key on the board.
/// </summary>
public static class UpdateBoard
{
    public sealed record Request(
        string Name, string? Emoji, string? Description, string Color, string RowVersion, string? KeyPrefix = null);

    public sealed record Response(
        Guid Id, string Name, string? Emoji, string? Description, string Color, string KeyPrefix, string RowVersion);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(120);
            RuleFor(r => r.Emoji).MaximumLength(16);
            RuleFor(r => r.Description).MaximumLength(500);
            RuleFor(r => r.Color).NotEmpty().HexColor();
            RuleFor(r => r.RowVersion).RowVersion();
            RuleFor(r => r.KeyPrefix)
                .Matches($"^[A-Z]{{{CardKeys.MinLength},{CardKeys.MaxLength}}}$")
                .When(r => r.KeyPrefix is not null)
                .WithMessage($"Use {CardKeys.MinLength} to {CardKeys.MaxLength} capital letters A to Z.");
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}", Handle)
                .WithTags("Boards")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, NotFound, ProblemHttpResult>> Handle(
            Guid boardId, Request request, AppDbContext db, IBoardAccess access, IActivityRecorder activity, CancellationToken ct)
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

            var board = await db.Boards.FirstAsync(b => b.Id == boardId, ct);
            ValidationRules.TryDecode(request.RowVersion, out var rowVersion);
            db.Entry(board).Property(b => b.RowVersion).OriginalValue = rowVersion;

            var before = new { board.Name, board.Emoji, board.Description, board.Color, board.KeyPrefix };
            board.Name = request.Name.Trim();
            board.Emoji = string.IsNullOrWhiteSpace(request.Emoji) ? null : request.Emoji;
            board.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            board.Color = request.Color.ToUpperInvariant();
            board.KeyPrefix = request.KeyPrefix ?? board.KeyPrefix;

            var fields = new (string Name, bool Changed)[]
                {
                    ("name", before.Name != board.Name),
                    ("emoji", before.Emoji != board.Emoji),
                    ("description", before.Description != board.Description),
                    ("color", before.Color != board.Color),
                    ("keyPrefix", before.KeyPrefix != board.KeyPrefix),
                }
                .Where(f => f.Changed)
                .Select(f => f.Name)
                .ToArray();
            if (fields.Length > 0)
            {
                activity.Record(ActivityTypes.BoardUpdated, boardId, null, new
                {
                    name = board.Name,
                    fields,
                    from = before.Name != board.Name ? before.Name : null,
                });
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return BoardResults.Conflict("The board was changed by someone else. Reload and try again.");
            }

            return TypedResults.Ok(new Response(
                board.Id, board.Name, board.Emoji, board.Description, board.Color, board.KeyPrefix, Convert.ToBase64String(board.RowVersion)));
        }
    }
}
