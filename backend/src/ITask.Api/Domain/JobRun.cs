namespace ITask.Api.Domain;

/// <summary>When a scheduled maintenance job last finished.</summary>
public class JobRun
{
    public const int NameMaxLength = 64;

    public required string Name { get; set; }

    public DateTimeOffset LastRunAt { get; set; }
}
