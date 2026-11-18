using ITask.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Maintenance;

/// <summary>Deletes activity entries older than <see cref="MaintenanceOptions.ActivityRetentionDays"/>.</summary>
internal sealed class ActivityRetentionJob(AppDbContext db, IOptions<MaintenanceOptions> options) : IMaintenanceJob
{
    public string Name => "activity-retention";

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now.AddDays(-options.Value.ActivityRetentionDays);
        await db.Activity.Where(a => a.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
    }
}
