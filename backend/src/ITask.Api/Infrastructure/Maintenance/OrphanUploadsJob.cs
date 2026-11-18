using ITask.Api.Common.Persistence;
using ITask.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Maintenance;

/// <summary>
/// Deletes blobs that never got an attachment row: uploads whose confirm call never came, or
/// files left behind by a failed delete.
/// </summary>
internal sealed class OrphanUploadsJob(AppDbContext db, IBlobStorage storage, IOptions<MaintenanceOptions> options) : IMaintenanceJob
{
    private const int BatchSize = 200;

    public string Name => "orphan-uploads";

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var batch = new List<string>(BatchSize);
        await foreach (var name in storage.ListNamesAsync(now - options.Value.OrphanUploadAge, ct))
        {
            batch.Add(name);
            if (batch.Count == BatchSize)
            {
                await DeleteOrphansAsync(batch, ct);
                batch.Clear();
            }
        }

        await DeleteOrphansAsync(batch, ct);
    }

    private async Task DeleteOrphansAsync(List<string> names, CancellationToken ct)
    {
        if (names.Count == 0)
        {
            return;
        }

        var known = await db.Attachments.Where(a => names.Contains(a.BlobName)).Select(a => a.BlobName).ToListAsync(ct);
        foreach (var name in names.Except(known, StringComparer.Ordinal))
        {
            await storage.DeleteAsync(name, ct);
        }
    }
}
