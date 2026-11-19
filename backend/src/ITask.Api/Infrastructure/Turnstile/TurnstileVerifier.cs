using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Turnstile;

public sealed class TurnstileOptions
{
    public const string Section = "Turnstile";

    /// <summary>
    /// Cloudflare Turnstile secret. When empty, verification is skipped outside Production (dev and tests)
    /// and every check fails in Production, so a missing secret never leaves sign-in unprotected.
    /// </summary>
    public string? SecretKey { get; set; }
}

/// <summary>Verifies a Cloudflare Turnstile token against the siteverify endpoint.</summary>
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, IPAddress? remoteIp, CancellationToken ct);
}

internal sealed partial class TurnstileVerifier(
    HttpClient http, IOptions<TurnstileOptions> options, IHostEnvironment env, ILogger<TurnstileVerifier> logger) : ITurnstileVerifier
{
    private static readonly Uri SiteVerify = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public async Task<bool> VerifyAsync(string? token, IPAddress? remoteIp, CancellationToken ct)
    {
        var secret = options.Value.SecretKey;
        if (string.IsNullOrEmpty(secret))
        {
            if (!env.IsProduction())
            {
                return true;
            }

            LogMissingSecret(logger);
            return false;
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var form = new Dictionary<string, string> { ["secret"] = secret, ["response"] = token };
        if (remoteIp is not null)
        {
            form["remoteip"] = remoteIp.ToString();
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(SiteVerify, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<SiteVerifyResult>(ct);
        return result?.Success == true;
    }

    private sealed record SiteVerifyResult([property: JsonPropertyName("success")] bool Success);

    [LoggerMessage(Level = LogLevel.Error, Message = "Turnstile:SecretKey is not set; human verification fails until it is.")]
    private static partial void LogMissingSecret(ILogger logger);
}
