namespace ITask.Api.Infrastructure.Maintenance;

public sealed class MaintenanceOptions
{
    public const string Section = "Maintenance";

    /// <summary>Runs <see cref="MaintenanceService"/>; off in tests.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often a running replica checks for due jobs.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Activity entries older than this many days are deleted.</summary>
    public int ActivityRetentionDays { get; set; } = 180;

    /// <summary>Blobs without an attachment row are deleted once they are this old.</summary>
    public TimeSpan OrphanUploadAge { get; set; } = TimeSpan.FromHours(24);
}
