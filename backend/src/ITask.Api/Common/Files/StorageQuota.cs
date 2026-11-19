using ITask.Api.Common.Auth;
using ITask.Api.Common.Persistence;
using ITask.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Files;

/// <summary>Caps stored attachment bytes per uploader and for the whole app (Blob Storage bills by size).</summary>
public static class StorageQuota
{
    /// <summary>Null when <paramref name="incomingBytes"/> more still fit; otherwise a 409 ProblemDetails.</summary>
    public static async Task<ProblemHttpResult?> CheckAsync(
        AppDbContext db, Guid userId, long incomingBytes, StorageOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(options);

        var mine = await db.Attachments.Where(a => a.UploadedById == userId).SumAsync(a => (long?)a.SizeBytes, ct) ?? 0;
        if (mine + incomingBytes > options.QuotaBytesPerUser)
        {
            return BoardResults.Conflict(
                $"You've reached your storage limit of {options.QuotaBytesPerUser / (1024 * 1024)} MB. Delete some files first.");
        }

        var all = await db.Attachments.SumAsync(a => (long?)a.SizeBytes, ct) ?? 0;
        return all + incomingBytes > options.QuotaBytesTotal
            ? BoardResults.Conflict("iTask's file storage is full right now. Try again later.")
            : null;
    }
}
