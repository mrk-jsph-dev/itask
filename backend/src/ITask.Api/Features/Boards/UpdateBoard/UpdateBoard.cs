using FluentValidation;
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
/// otherwise someone else saved first and the response is 409.
/// </summary>
public static class UpdateBoard
{
    public sealed record Request(string Name, string? Emoji, string? Description, string Color, string RowVersion);

    public sealed record Response(Guid Id, string Name, string? Emoji, string? Description, string Color, string RowVersion);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(120);
            RuleFor(r => r.Emoji).MaximumLength(16);
            RuleFor(r => r.Description).MaximumLength(500);
            RuleFor(r => r.Color).NotEmpty().HexColor();
            RuleFor(r => r.RowVersion).RowVersion();
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
            Guid boardId, Request request, AppDbContext db, IBoardAccess access, CancellationToken ct)
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

            board.Name = request.Name.Trim();
            board.Emoji = string.IsNullOrWhiteSpace(request.Emoji) ? null : request.Emoji;
            board.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            board.Color = request.Color.ToUpperInvariant();

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return BoardResults.Conflict("The board was changed by someone else. Reload and try again.");
            }

            // activity: board updated

            return TypedResults.Ok(new Response(
                board.Id, board.Name, board.Emoji, board.Description, board.Color, Convert.ToBase64String(board.RowVersion)));
        }
    }
}
