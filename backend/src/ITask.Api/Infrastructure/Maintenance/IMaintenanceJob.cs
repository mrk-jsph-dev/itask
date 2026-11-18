namespace ITask.Api.Infrastructure.Maintenance;

/// <summary>A daily job run by <see cref="MaintenanceRunner"/>.</summary>
public interface IMaintenanceJob
{
    /// <summary>Key of the job's <see cref="Domain.JobRun"/> row and lock.</summary>
    string Name { get; }

    Task RunAsync(DateTimeOffset now, CancellationToken ct);
}
