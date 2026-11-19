namespace ITask.Api.Domain;

/// <summary>How often something happened on one UTC day, e.g. sign-ups or AI calls across all users.</summary>
public class UsageCounter
{
    public const int NameMaxLength = 80;

    public required string Name { get; set; }

    public DateOnly Date { get; set; }

    public int Count { get; set; }
}
