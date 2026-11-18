using ITask.Api.Common.Persistence;
using ITask.Api.Domain;

namespace ITask.Api.Infrastructure.Maintenance;

/// <summary>
/// Runs each <see cref="IMaintenanceJob"/> once per daily slot (03:00 UTC). A job whose slot passed
/// while no replica was running runs on the next check, so scale-to-zero only delays it. The
/// due check and the run share one database lock, so two replicas never run the same job.
/// </summary>
public sealed partial class MaintenanceRunner(AppDbContext db, IEnumerable<IMaintenanceJob> jobs, ILogger<MaintenanceRunner> logger)
{
    public static readonly TimeSpan DailyAt = TimeSpan.FromHours(3);

    /// <summary>Runs the jobs that are due at <paramref name="now"/> and returns their names.</summary>
    public async Task<IReadOnlyList<string>> RunDueAsync(DateTimeOffset now, CancellationToken ct)
    {
        var slot = LastSlot(now);
        var ran = new List<string>();

        foreach (var job in jobs)
        {
            var done = false;
            try
            {
                await JobLock.TryRunAsync(db, job.Name, async token =>
                {
                    var run = await db.JobRuns.FindAsync([job.Name], token);
                    if (run is not null && run.LastRunAt >= slot)
                    {
                        return;
                    }

                    await job.RunAsync(now, token);

                    if (run is null)
                    {
                        db.JobRuns.Add(new JobRun { Name = job.Name, LastRunAt = now });
                    }
                    else
                    {
                        run.LastRunAt = now;
                    }

                    await db.SaveChangesAsync(token);
                    done = true;
                }, ct);
            }
#pragma warning disable CA1031 // One failing job must not stop the others; it retries on the next check.
            catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogJobFailed(logger, e, job.Name);
                db.ChangeTracker.Clear();
            }

            if (done)
            {
                LogJobRan(logger, job.Name);
                ran.Add(job.Name);
            }
        }

        return ran;
    }

    /// <summary>The most recent daily slot at or before <paramref name="now"/>.</summary>
    public static DateTimeOffset LastSlot(DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero) + DailyAt;
        return today <= now ? today : today.AddDays(-1);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Maintenance job {Job} finished.")]
    private static partial void LogJobRan(ILogger logger, string job);

    [LoggerMessage(Level = LogLevel.Error, Message = "Maintenance job {Job} failed.")]
    private static partial void LogJobFailed(ILogger logger, Exception exception, string job);
}
