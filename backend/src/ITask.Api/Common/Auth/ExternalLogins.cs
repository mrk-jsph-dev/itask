using System.Net.Http.Headers;
using System.Net.Mail;
using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Common.Auth;

/// <summary>An external sign-in provider: <see cref="Id"/> is used in URLs, <see cref="Scheme"/> in AspNetUserLogins.</summary>
public sealed record ExternalProvider(string Id, string Scheme);

public static class ExternalLogins
{
    /// <summary>Claim holding an email the provider says the account owns (only set when verified).</summary>
    public const string VerifiedEmailClaim = "itask:verified_email";

    /// <summary>Claim with the account's label shown in the profile (GitHub login, Microsoft email).</summary>
    public const string AccountNameClaim = "itask:account_name";

    public static readonly ExternalProvider GitHub = new("github", GitHubAuthenticationDefaults.AuthenticationScheme);

    public static readonly ExternalProvider Microsoft = new("microsoft", MicrosoftAccountDefaults.AuthenticationScheme);

    public static IReadOnlyList<ExternalProvider> All { get; } = [GitHub, Microsoft];

    public static ExternalProvider? FindById(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public static ExternalProvider? FindByScheme(string? scheme) =>
        All.FirstOrDefault(p => string.Equals(p.Scheme, scheme, StringComparison.Ordinal));

    /// <summary>Providers whose handler is registered, i.e. that have a client id and secret configured.</summary>
    public static async Task<IReadOnlyList<ExternalProvider>> EnabledAsync(IAuthenticationSchemeProvider schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);
        var enabled = new List<ExternalProvider>();
        foreach (var provider in All)
        {
            if (await schemes.GetSchemeAsync(provider.Scheme) is not null)
            {
                enabled.Add(provider);
            }
        }

        return enabled;
    }

    /// <summary>
    /// Registers GitHub and Microsoft sign-in when <c>Auth:GitHub:*</c> / <c>Auth:Microsoft:*</c> hold a
    /// client id and secret. Both sign in to Identity's external cookie; the callback slice takes it from there.
    /// </summary>
    public static IServiceCollection AddExternalLogins(this IServiceCollection services, IConfiguration config)
    {
        var auth = services.AddAuthentication();

        if (Credentials(config, "GitHub") is var (githubId, githubSecret))
        {
            auth.AddGitHub(options =>
            {
                options.ClientId = githubId;
                options.ClientSecret = githubSecret;
                options.CallbackPath = "/api/auth/signin-github";
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.Scope.Add("user:email");
                options.Events.OnCreatingTicket = AddGitHubClaimsAsync;
            });
        }

        if (Credentials(config, "Microsoft") is var (microsoftId, microsoftSecret))
        {
            auth.AddMicrosoftAccount(options =>
            {
                options.ClientId = microsoftId;
                options.ClientSecret = microsoftSecret;
                options.CallbackPath = "/api/auth/signin-microsoft";
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.Events.OnCreatingTicket = AddMicrosoftClaims;
            });
        }

        return services;
    }

    private static (string Id, string Secret)? Credentials(IConfiguration config, string name) =>
        config[$"Auth:{name}:ClientId"] is { Length: > 0 } id && config[$"Auth:{name}:ClientSecret"] is { Length: > 0 } secret
            ? (id, secret)
            : null;

    /// <summary>The public profile email may be unverified, so the primary verified one comes from /user/emails.</summary>
    private static async Task AddGitHubClaimsAsync(OAuthCreatingTicketContext context)
    {
        var identity = context.Identity!;
        if (identity.FindFirst(ClaimTypes.Name)?.Value is { } login)
        {
            identity.AddClaim(new Claim(AccountNameClaim, login));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, GitHubAuthenticationDefaults.UserEmailsEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await context.Backchannel.SendAsync(request, context.HttpContext.RequestAborted);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(context.HttpContext.RequestAborted);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: context.HttpContext.RequestAborted);
        foreach (var entry in json.RootElement.EnumerateArray())
        {
            if (entry.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True
                && entry.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True
                && entry.TryGetProperty("email", out var email) && email.GetString() is { } address)
            {
                identity.AddClaim(new Claim(VerifiedEmailClaim, address));
                break;
            }
        }
    }

    /// <summary>
    /// Graph's <c>mail</c> is editable by tenant admins and not verified; the user principal name's domain is
    /// verified by the tenant (or is the Microsoft account's sign-in email), so that is the trusted address.
    /// Guest UPNs (<c>#EXT#</c>) belong to the host tenant and are not used.
    /// </summary>
    private static Task AddMicrosoftClaims(OAuthCreatingTicketContext context)
    {
        if (context.User.TryGetProperty("userPrincipalName", out var upn)
            && upn.GetString() is { } address
            && !address.Contains("#EXT#", StringComparison.OrdinalIgnoreCase)
            && MailAddress.TryCreate(address, out var parsed)
            && parsed.Address == address)
        {
            context.Identity!.AddClaim(new Claim(VerifiedEmailClaim, address));
            context.Identity.AddClaim(new Claim(AccountNameClaim, address));
        }

        return Task.CompletedTask;
    }
}
