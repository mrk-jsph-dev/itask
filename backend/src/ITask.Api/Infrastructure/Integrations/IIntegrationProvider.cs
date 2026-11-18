namespace ITask.Api.Infrastructure.Integrations;

/// <summary>
/// Board integrations are optional and per board; a board can hold many connections,
/// including several of the same kind. Each tool is a provider implementing this
/// interface. v1 providers: GitHub (GitHub App) and Azure (Entra ID, Reader role,
/// Azure Monitor action-group webhook). Others plug in later without touching the core.
/// </summary>
public interface IIntegrationProvider
{
    /// <summary>Stable key stored on the connection, e.g. "github" or "azure".</summary>
    string Key { get; }

    /// <summary>Verifies the webhook's signature/secret before anything else runs.</summary>
    Task<bool> VerifyWebhookAsync(HttpRequest request, Guid connectionId, CancellationToken ct);

    /// <summary>Turns a verified webhook into card changes via the board's rules.</summary>
    Task HandleWebhookAsync(HttpRequest request, Guid connectionId, CancellationToken ct);
}
