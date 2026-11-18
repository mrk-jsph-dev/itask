using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Attachments.GetDownloadUrl;

/// <summary>A 5-minute read link that downloads the file under its original name. Any member.</summary>
public static class GetDownloadUrl
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public sealed record Response(Uri Url, DateTimeOffset ExpiresAt);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/boards/{boardId:guid}/cards/{cardId:guid}/attachments/{attachmentId:guid}/url", Handle)
                .WithTags("Attachments")
                .RequireAuthorization();

        private static async Task<Results<Ok<Response>, ProblemHttpResult>> Handle(
            Guid boardId,
            Guid cardId,
            Guid attachmentId,
            AppDbContext db,
            IBoardAccess access,
            IBlobStorage storage,
            CancellationToken ct)
        {
            if (await access.GetRoleAsync(boardId, ct) is null)
            {
                return CardGuard.NotFound();
            }

            var file = await db.Attachments
                .AsNoTracking()
                .Where(a => a.Id == attachmentId && a.CardId == cardId && a.Card!.BoardId == boardId)
                .Select(a => new { a.BlobName, a.FileName })
                .FirstOrDefaultAsync(ct);
            if (file is null)
            {
                return CardGuard.NotFound();
            }

            var url = await storage.CreateDownloadUrlAsync(file.BlobName, file.FileName, Lifetime, ct);
            return TypedResults.Ok(new Response(url.Url, url.ExpiresAt));
        }
    }
}
