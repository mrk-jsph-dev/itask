using ITask.Api.Common.Persistence;
using ITask.Api.Infrastructure.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Infrastructure.Notifications;

/// <summary>Deletes read notifications after <see cref="ReadAge"/> and all of them after <see cref="MaxAge"/>.</summary>
internal sealed class NotificationRetentionJob(AppDbContext db) : IMaintenanceJob
{
    public static readonly TimeSpan ReadAge = TimeSpan.FromDays(90);

    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(180);

    public string Name => "notification-retention";

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var readCutoff = now - ReadAge;
        var cutoff = now - MaxAge;
        await db.Notifications
            .Where(n => n.CreatedAt < cutoff || (n.ReadAt != null && n.CreatedAt < readCutoff))
            .ExecuteDeleteAsync(ct);
    }
}
