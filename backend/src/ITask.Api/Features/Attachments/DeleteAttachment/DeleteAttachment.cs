using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Attachments.DeleteAttachment;

/// <summary>Removes a file and its blob. Editor or above, and only the uploader or an Owner.</summary>
public static class DeleteAttachment
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/boards/{boardId:guid}/cards/{cardId:guid}/attachments/{attachmentId:guid}", Handle)
                .WithTags("Attachments")
                .RequireAuthorization();

        private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
            Guid boardId,
            Guid cardId,
            Guid attachmentId,
            AppDbContext db,
            IBoardAccess access,
            IBlobStorage storage,
            ICurrentUser user,
            IActivityRecorder activity,
            CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            var attachment = await db.Attachments
                .FirstOrDefaultAsync(a => a.Id == attachmentId && a.CardId == cardId && a.Card!.BoardId == boardId, ct);
            if (attachment is null)
            {
                return CardGuard.NotFound();
            }

            if (attachment.UploadedById != user.Id && !await access.HasAtLeastAsync(boardId, BoardRole.Owner, ct))
            {
                return BoardResults.Forbidden();
            }

            await storage.DeleteAsync(attachment.BlobName, ct);
            db.Attachments.Remove(attachment);
            await activity.RecordCardAsync(ActivityTypes.AttachmentDeleted, boardId, cardId, new { fileName = attachment.FileName }, ct);
            await db.SaveChangesAsync(ct);

            return TypedResults.NoContent();
        }
    }
}
