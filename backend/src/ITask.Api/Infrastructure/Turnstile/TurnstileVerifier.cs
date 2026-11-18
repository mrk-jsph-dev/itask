using System.Net;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Turnstile;

public sealed class TurnstileOptions
{
    public const string Section = "Turnstile";

    /// <summary>Cloudflare Turnstile secret. When empty, verification is skipped (dev and tests).</summary>
    public string? SecretKey { get; set; }
}

/// <summary>Verifies a Cloudflare Turnstile token against the siteverify endpoint.</summary>
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, IPAddress? remoteIp, CancellationToken ct);
}

internal sealed class TurnstileVerifier(HttpClient http, IOptions<TurnstileOptions> options) : ITurnstileVerifier
{
    private static readonly Uri SiteVerify = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public async Task<bool> VerifyAsync(string? token, IPAddress? remoteIp, CancellationToken ct)
    {
        var secret = options.Value.SecretKey;
        if (string.IsNullOrEmpty(secret))
        {
            return true;
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
}
