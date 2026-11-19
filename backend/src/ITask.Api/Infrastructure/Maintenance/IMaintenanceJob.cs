namespace ITask.Api.Infrastructure.Maintenance;

/// <summary>A scheduled job run by <see cref="MaintenanceRunner"/>.</summary>
public interface IMaintenanceJob
{
    /// <summary>Key of the job's <see cref="Domain.JobRun"/> row and lock.</summary>
    string Name { get; }

    Task RunAsync(DateTimeOffset now, CancellationToken ct);

    /// <summary>The latest slot at or before <paramref name="now"/>; the job runs once per slot. Daily at 03:00 UTC unless overridden.</summary>
    DateTimeOffset Slot(DateTimeOffset now) => MaintenanceRunner.LastSlot(now);
}
