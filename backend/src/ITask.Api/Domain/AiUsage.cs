namespace ITask.Api.Domain;

/// <summary>One user's AI use on one UTC day; <see cref="Requests"/> is checked against the daily cap.</summary>
public class AiUsage
{
    public Guid UserId { get; set; }

    public DateOnly Date { get; set; }

    public int Requests { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }
}
