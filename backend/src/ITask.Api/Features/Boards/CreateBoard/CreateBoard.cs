using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Boards.CreateBoard;

/// <summary>
/// Creates a board and makes the caller its Owner.
/// </summary>
public static class CreateBoard
{
    public sealed record Request(string Name, string? Emoji, string? Description = null, string? Color = null);

    public sealed record Response(Guid Id, string Name, string? Emoji, string? Description, string Color, BoardRole Role);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(120);
            RuleFor(r => r.Emoji).MaximumLength(16);
            RuleFor(r => r.Description).MaximumLength(500);
            RuleFor(r => r.Color).HexColor().When(r => r.Color is not null);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards", Handle)
                .WithTags("Boards")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Created<Response>> Handle(
            Request request, AppDbContext db, ICurrentUser user, CancellationToken ct)
        {
            var board = new Board
            {
                Name = request.Name.Trim(),
                Emoji = string.IsNullOrWhiteSpace(request.Emoji) ? null : request.Emoji,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Color = request.Color?.ToUpperInvariant() ?? Board.DefaultColor,
            };
            board.Members.Add(new BoardMember { UserId = user.Id, Role = BoardRole.Owner });

            db.Boards.Add(board);
            await db.SaveChangesAsync(ct);

            return TypedResults.Created(
                $"/api/boards/{board.Id}",
                new Response(board.Id, board.Name, board.Emoji, board.Description, board.Color, BoardRole.Owner));
        }
    }
}
