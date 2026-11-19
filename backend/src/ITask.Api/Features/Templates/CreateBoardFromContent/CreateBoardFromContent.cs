using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Common.Templates;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Templates.CreateBoardFromContent;

/// <summary>
/// Creates a board from lists and card skeletons sent by the client (e.g. an accepted AI draft)
/// and makes the caller its Owner.
/// </summary>
public static class CreateBoardFromContent
{
    public sealed record Request(string Name, TemplateContent Content, string? Emoji = null, string? Description = null, string? Color = null);

    public sealed record Response(Guid Id, string Name, string? Emoji, string? Description, string Color, BoardRole Role);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Name).NotEmpty().MaximumLength(120);
            RuleFor(r => r.Emoji).MaximumLength(16);
            RuleFor(r => r.Description).MaximumLength(500);
            RuleFor(r => r.Color).HexColor().When(r => r.Color is not null);
            RuleFor(r => r.Content).NotNull();
            RuleFor(r => r.Content.Lists).NotEmpty().Must(l => l.Count <= BoardList.MaxActivePerBoard)
                .When(r => r.Content is not null);
            RuleForEach(r => r.Content.Lists).ChildRules(list =>
            {
                list.RuleFor(l => l.Name).NotEmpty().MaximumLength(BoardList.NameMaxLength);
                list.RuleFor(l => l.Emoji).MaximumLength(16);
                list.RuleFor(l => l.WipLimit).InclusiveBetween(1, 999).When(l => l.WipLimit is not null);
            }).When(r => r.Content?.Lists is not null);
            RuleFor(r => r.Content.Labels).Must(l => l is null || l.Count <= Label.MaxPerBoard)
                .When(r => r.Content is not null);
            RuleForEach(r => r.Content.Labels).ChildRules(label =>
            {
                label.RuleFor(l => l.Name).NotEmpty().MaximumLength(Label.NameMaxLength);
                label.RuleFor(l => l.Color).HexColor();
                label.RuleFor(l => l.Emoji).MaximumLength(16);
            }).When(r => r.Content?.Labels is not null);
            RuleFor(r => r.Content.Cards).Must(c => c is null || c.Count <= TemplateContent.MaxCards)
                .When(r => r.Content is not null);
            RuleForEach(r => r.Content.Cards).ChildRules(card =>
            {
                card.RuleFor(c => c.List).NotEmpty();
                card.RuleFor(c => c.Title).NotEmpty().MaximumLength(Card.TitleMaxLength);
                card.RuleFor(c => c.Emoji).MaximumLength(Card.EmojiMaxLength);
                card.RuleFor(c => c.Description).MaximumLength(Card.DescriptionMaxLength);
                card.RuleFor(c => c.Priority).IsInEnum();
                card.RuleFor(c => c.Checklist).Must(c => c is null || c.Count <= ChecklistItem.MaxPerCard);
                card.RuleForEach(c => c.Checklist).NotEmpty().MaximumLength(ChecklistItem.TextMaxLength);
            }).When(r => r.Content?.Cards is not null);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/from-content", Handle)
                .WithTags("Templates")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Created<Response>, ProblemHttpResult>> Handle(
            Request request, AppDbContext db, ICurrentUser user, IActivityRecorder activity, CancellationToken ct)
        {
            if (await DataLimits.CheckOwnedBoardsAsync(db, user.Id, ct) is { } full)
            {
                return full;
            }

            var name = request.Name.Trim();
            var board = new Board
            {
                Name = name,
                KeyPrefix = CardKeys.DefaultPrefix(name),
                Emoji = string.IsNullOrWhiteSpace(request.Emoji) ? null : request.Emoji,
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                Color = (request.Color ?? Board.DefaultColor).ToUpperInvariant(),
            };
            board.Members.Add(new BoardMember { UserId = user.Id, Role = BoardRole.Owner });
            request.Content.ApplyTo(board, user.Id);

            db.Boards.Add(board);
            activity.Record(ActivityTypes.BoardCreated, board.Id, null, new { name = board.Name });
            await db.SaveChangesAsync(ct);

            return TypedResults.Created(
                $"/api/boards/{board.Id}",
                new Response(board.Id, board.Name, board.Emoji, board.Description, board.Color, BoardRole.Owner));
        }
    }
}
