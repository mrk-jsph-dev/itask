using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Comments.AddComment;

/// <summary>Posts a markdown comment. Any member, viewers included. <c>@DisplayName</c> mentions board members.</summary>
public static class AddComment
{
    public sealed record Request(string Body);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.Body).NotEmpty().MaximumLength(Comment.BodyMaxLength);
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/cards/{cardId:guid}/comments", Handle)
                .WithTags("Comments")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Created<CommentView>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Request request, AppDbContext db, IBoardAccess access, ICurrentUser user, CancellationToken ct)
        {
            if ((await CardGuard.CheckMemberAsync(boardId, db, access, ct)).Denied is { } denied)
            {
                return denied;
            }

            if (!await CardGuard.CardExistsAsync(db, boardId, cardId, ct))
            {
                return CardGuard.NotFound();
            }

            var comment = new Comment { CardId = cardId, AuthorId = user.Id, Body = request.Body.Trim() };
            await CommentMentions.SyncAsync(db, boardId, comment, ct);
            db.Comments.Add(comment);
            await db.SaveChangesAsync(ct);

            // activity: comment added

            return TypedResults.Created(
                $"/api/boards/{boardId}/cards/{cardId}/comments/{comment.Id}",
                await CommentMentions.ViewAsync(db, comment.Id, ct));
        }
    }
}
