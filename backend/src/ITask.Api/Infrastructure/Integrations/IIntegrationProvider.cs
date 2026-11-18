namespace ITask.Api.Infrastructure.Integrations;

/// <summary>
/// An external tool a board can connect to (one board may hold several connections,
/// including several of the same provider). Webhooks are routed to the provider by
/// <see cref="Key"/> and must pass <see cref="VerifyWebhookAsync"/> before handling.
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
