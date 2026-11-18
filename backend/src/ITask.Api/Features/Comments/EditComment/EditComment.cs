using FluentValidation;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Comments.EditComment;

/// <summary>Replaces a comment's body. Author only; mentions are re-read from the new body.</summary>
public static class EditComment
{
    public sealed record Request(string Body);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.Body).NotEmpty().MaximumLength(Comment.BodyMaxLength);
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/boards/{boardId:guid}/cards/{cardId:guid}/comments/{commentId:guid}", Handle)
                .WithTags("Comments")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<CommentView>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId,
            Guid cardId,
            Guid commentId,
            Request request,
            AppDbContext db,
            IBoardAccess access,
            ICurrentUser user,
            IActivityRecorder activity,
            TimeProvider time,
            CancellationToken ct)
        {
            if ((await CardGuard.CheckMemberAsync(boardId, db, access, ct)).Denied is { } denied)
            {
                return denied;
            }

            var comment = await db.Comments
                .Include(c => c.Mentions)
                .FirstOrDefaultAsync(c => c.Id == commentId && c.CardId == cardId && c.Card!.BoardId == boardId, ct);
            if (comment is null)
            {
                return CardGuard.NotFound();
            }

            if (comment.AuthorId != user.Id)
            {
                return TypedResults.Problem(
                    title: "Only the author can edit a comment.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            comment.Body = request.Body.Trim();
            comment.EditedAt = time.GetUtcNow();
            await CommentMentions.SyncAsync(db, boardId, comment, ct);
            await activity.RecordCardAsync(ActivityTypes.CommentEdited, boardId, cardId, new { commentId }, ct);
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(await CommentMentions.ViewAsync(db, comment.Id, ct));
        }
    }
}
