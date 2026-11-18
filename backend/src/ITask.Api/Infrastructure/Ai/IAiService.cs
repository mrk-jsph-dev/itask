namespace ITask.Api.Infrastructure.Ai;

/// <summary>
/// Chat-completion abstraction used by AI features. Implementations target an Azure
/// OpenAI (Foundry) deployment configured under <see cref="AiOptions"/>; callers must
/// enforce <see cref="AiOptions.DailyRequestsPerUser"/> before calling.
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

/// <summary>Registered when <c>Ai:Enabled</c> is false; any call is a programming error.</summary>
internal sealed class DisabledAiService : IAiService
{
    public bool IsEnabled => false;

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
        => throw new InvalidOperationException("AI is not configured for this environment.");
}
