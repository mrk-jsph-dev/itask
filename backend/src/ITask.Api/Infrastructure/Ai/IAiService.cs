namespace ITask.Api.Infrastructure.Ai;

/// <summary>
/// All AI features go through this interface. The real implementation (release 1.2)
/// calls an OpenAI model deployed in Microsoft Foundry via the Azure.AI.OpenAI SDK,
/// authenticated with the API's managed identity. AI only runs when a user presses
/// an AI button, never in the background, and is capped per user per day.
/// </summary>
public interface IAiService
{
    bool IsEnabled { get; }

    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}

public sealed class AiOptions
{
    public const string Section = "Ai";

    public bool Enabled { get; set; }

    /// <summary>Foundry / Azure OpenAI endpoint, e.g. https://&lt;name&gt;.openai.azure.com/.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Deployment name of the OpenAI model in Foundry.</summary>
    public string? Deployment { get; set; }

    public int DailyRequestsPerUser { get; set; } = 20;
}

/// <summary>Used until the Foundry deployment is configured.</summary>
internal sealed class DisabledAiService : IAiService
{
    public bool IsEnabled => false;

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
        => throw new InvalidOperationException("AI is not configured for this environment.");
}
