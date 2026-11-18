using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Comments.DeleteComment;

/// <summary>Removes a comment for good. Its author, or a board Owner for any comment.</summary>
public static class DeleteComment
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/boards/{boardId:guid}/cards/{cardId:guid}/comments/{commentId:guid}", Handle)
                .WithTags("Comments")
                .RequireAuthorization();

        private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
            Guid boardId, Guid cardId, Guid commentId, AppDbContext db, IBoardAccess access, ICurrentUser user, IActivityRecorder activity, CancellationToken ct)
        {
            var (role, denied) = await CardGuard.CheckMemberAsync(boardId, db, access, ct);
            if (denied is not null)
            {
                return denied;
            }

            var comment = await db.Comments
                .FirstOrDefaultAsync(c => c.Id == commentId && c.CardId == cardId && c.Card!.BoardId == boardId, ct);
            if (comment is null)
            {
                return CardGuard.NotFound();
            }

            if (comment.AuthorId != user.Id && role != BoardRole.Owner)
            {
                return BoardResults.Forbidden();
            }

            db.Comments.Remove(comment);
            await activity.RecordCardAsync(ActivityTypes.CommentDeleted, boardId, cardId, new { commentId }, ct);
            await db.SaveChangesAsync(ct);

            return TypedResults.NoContent();
        }
    }
}
