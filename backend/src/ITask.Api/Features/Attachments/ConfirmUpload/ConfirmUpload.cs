using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Files;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Attachments.ConfirmUpload;

/// <summary>
/// Step two of an upload: records the file once its blob exists. Size and type are read from
/// storage, not trusted from the client; a blob that breaks the rules is deleted and the response
/// is 400. Editor or above, not demo accounts.
/// </summary>
public static class ConfirmUpload
{
    public sealed record Request(string BlobName, string FileName);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.BlobName).NotEmpty().MaximumLength(Attachment.BlobNameMaxLength);
            RuleFor(r => r.FileName).NotEmpty().MaximumLength(Attachment.FileNameMaxLength);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/cards/{cardId:guid}/attachments", Handle)
                .WithTags("Attachments")
                .RequireAuthorization()
                .RequireNonDemo()
                .WithValidation<Request>();

        private static async Task<Results<Created<AttachmentView>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId,
            Guid cardId,
            Request request,
            AppDbContext db,
            IBoardAccess access,
            IBlobStorage storage,
            ICurrentUser user,
            CancellationToken ct)
        {
            if (await CardGuard.CheckEditorAsync(boardId, db, access, ct) is { } denied)
            {
                return denied;
            }

            if (!await CardGuard.CardExistsAsync(db, boardId, cardId, ct))
            {
                return CardGuard.NotFound();
            }

            // Only names this card handed out, so a client can't claim another card's blob.
            var name = request.BlobName;
            var prefix = AttachmentRules.CardPrefix(boardId, cardId);
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || name.AsSpan(prefix.Length).ContainsAny('/', '.'))
            {
                return Invalid(nameof(Request.BlobName), "This upload does not belong to the card.");
            }

            if (await db.Attachments.AnyAsync(a => a.BlobName == name, ct))
            {
                return BoardResults.Conflict("This upload is already attached.");
            }

            var blob = await storage.GetInfoAsync(name, ct);
            if (blob is null)
            {
                return Invalid(nameof(Request.BlobName), "The file has not been uploaded yet.");
            }

            if (!AttachmentRules.IsAllowedSize(blob.SizeBytes))
            {
                await storage.DeleteAsync(name, ct);
                return Invalid("SizeBytes", "Files must be between 1 byte and 10 MB.");
            }

            if (!AttachmentRules.IsAllowedType(blob.ContentType))
            {
                await storage.DeleteAsync(name, ct);
                return Invalid("ContentType", "This file type is not allowed. Use images, PDF, text, Office documents or zip.");
            }

            if (await db.Attachments.CountAsync(a => a.CardId == cardId, ct) >= Attachment.MaxPerCard)
            {
                await storage.DeleteAsync(name, ct);
                return BoardResults.Conflict($"A card can have at most {Attachment.MaxPerCard} files.");
            }

            var attachment = new Attachment
            {
                CardId = cardId,
                FileName = Path.GetFileName(request.FileName.Trim()),
                ContentType = AttachmentRules.MediaType(blob.ContentType!),
                SizeBytes = blob.SizeBytes,
                BlobName = name,
                UploadedById = user.Id,
            };
            db.Attachments.Add(attachment);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A concurrent confirm of the same blob won the unique index.
                return BoardResults.Conflict("This upload is already attached.");
            }

            // activity: attachment added

            var view = await db.Attachments.AsNoTracking()
                .Where(a => a.Id == attachment.Id)
                .Select(AttachmentView.Project)
                .FirstAsync(ct);
            return TypedResults.Created($"/api/boards/{boardId}/cards/{cardId}/attachments/{attachment.Id}", view);
        }

        private static ValidationProblem Invalid(string field, string message) =>
            TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    }
}
