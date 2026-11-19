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
using Microsoft.Extensions.Options;

namespace ITask.Api.Features.Attachments.RequestUpload;

/// <summary>
/// Step one of an upload: checks the declared name, type and size, then returns a 10-minute URL
/// the browser PUTs the file to (header <c>x-ms-blob-type: BlockBlob</c>). Editor or above, not
/// demo accounts. Nothing is recorded until <c>ConfirmUpload</c>.
/// </summary>
public static class RequestUpload
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public sealed record Request(string FileName, string ContentType, long SizeBytes);

    public sealed record Response(string BlobName, Uri UploadUrl, DateTimeOffset ExpiresAt);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.FileName).NotEmpty().MaximumLength(Attachment.FileNameMaxLength);
            RuleFor(r => r.ContentType)
                .Must(AttachmentRules.IsAllowedType)
                .WithMessage("This file type is not allowed. Use images, PDF, text, Office documents or zip.");
            RuleFor(r => r.SizeBytes)
                .Must(AttachmentRules.IsAllowedSize)
                .WithMessage("Files must be between 1 byte and 10 MB.");
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/boards/{boardId:guid}/cards/{cardId:guid}/attachments/upload-url", Handle)
                .WithTags("Attachments")
                .RequireAuthorization()
                .RequireNonDemo()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, ValidationProblem, ProblemHttpResult>> Handle(
            Guid boardId,
            Guid cardId,
            Request request,
            AppDbContext db,
            IBoardAccess access,
            IBlobStorage storage,
            ICurrentUser user,
            IOptions<StorageOptions> storageOptions,
            DailyCounters counters,
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

            if (await db.Attachments.CountAsync(a => a.CardId == cardId, ct) >= Attachment.MaxPerCard)
            {
                return BoardResults.Conflict($"A card can have at most {Attachment.MaxPerCard} files.");
            }

            if (await StorageQuota.CheckAsync(db, user.Id, request.SizeBytes, storageOptions.Value, ct) is { } full)
            {
                return full;
            }

            if (!await counters.TryIncrementAsync($"uploads:{user.Id:N}", storageOptions.Value.UploadUrlsPerUserPerDay, ct))
            {
                return TypedResults.Problem(
                    title: "You've reached today's upload limit. Try again tomorrow.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var blobName = AttachmentRules.NewBlobName(boardId, cardId);
            var url = await storage.CreateUploadUrlAsync(blobName, Lifetime, ct);
            return TypedResults.Ok(new Response(blobName, url.Url, url.ExpiresAt));
        }
    }
}
