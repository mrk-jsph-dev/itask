namespace ITask.Api.Infrastructure.Ai;

/// <summary>
/// Chat-completion abstraction used by AI features. Implementations target an Azure
/// OpenAI (Foundry) deployment configured under <see cref="AiOptions"/>; callers must
/// consume a request from the user's daily quota (<c>AiQuota</c>) before calling.
/// </summary>
public interface IAiService
{
    bool IsEnabled { get; }

    Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct);
}

/// <summary>
/// One chat completion. With <see cref="JsonSchema"/> set the model must answer with JSON matching it
/// (strict structured output); otherwise it answers in plain text.
/// </summary>
public sealed record AiRequest(
    string System,
    string User,
    int MaxOutputTokens,
    string? JsonSchemaName = null,
    BinaryData? JsonSchema = null);

public sealed record AiResult(string Text, int InputTokens, int OutputTokens);

public sealed class AiOptions
{
    public const string Section = "Ai";

    public bool Enabled { get; set; }

    /// <summary>Foundry / Azure OpenAI endpoint, e.g. https://&lt;name&gt;.openai.azure.com/.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Deployment name of the OpenAI model in Foundry.</summary>
    public string? Deployment { get; set; }

    public int DailyRequestsPerUser { get; set; } = 20;

    public bool IsConfigured => Enabled && Endpoint is not null && !string.IsNullOrWhiteSpace(Deployment);
}

/// <summary>Registered when AI is not configured; any call is a programming error.</summary>
internal sealed class DisabledAiService : IAiService
{
    public bool IsEnabled => false;

    public Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct)
        => throw new InvalidOperationException("AI is not configured for this environment.");
}

public static class AiServiceExtensions
{
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(AiOptions.Section);
        services.Configure<AiOptions>(section);
        services.AddSingleton<AiPrompts>();

        if (section.Get<AiOptions>()?.IsConfigured == true)
        {
            services.AddSingleton<IAiService, AzureOpenAiService>();
        }
        else
        {
            services.AddSingleton<IAiService, DisabledAiService>();
        }

        return services;
    }
}
