using System.Net.Http.Json;
using System.Security.Claims;
using ITask.Api.Common.Auth;
using ITask.Api.Features.Auth.ExternalChallenge;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>What a provider handler would put in Identity's external cookie.</summary>
public sealed record FakeExternalIdentity(
    string Scheme,
    string Key,
    string Nonce,
    string? VerifiedEmail = null,
    string? Name = null,
    string? AccountName = null,
    string? LinkTicket = null);

/// <summary>
/// Enables GitHub and Microsoft with dummy credentials and stands in for the provider round trip:
/// <c>POST /test/external</c> signs in Identity's external cookie the way the OAuth handler does,
/// then the real callback runs. The clock can be moved forward.
/// </summary>
public sealed class ExternalLoginFactory : ApiFactory
{
    public const string SiteUrl = "https://app.test";

    public TestClock Clock { get; } = new();

    protected override Dictionary<string, string?> ExtraConfig => new() { ["Email:PublicUrl"] = SiteUrl };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Providers are registered while services are added, before app configuration overrides apply.
        builder.UseSetting("Auth:GitHub:ClientId", "github-id");
        builder.UseSetting("Auth:GitHub:ClientSecret", "github-secret");
        builder.UseSetting("Auth:Microsoft:ClientId", "microsoft-id");
        builder.UseSetting("Auth:Microsoft:ClientSecret", "microsoft-secret");
        base.ConfigureWebHost(builder);
    }

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IStartupFilter, FakeProviderStartup>();
    }

    /// <summary>Runs the callback for <paramref name="identity"/> and returns the SPA query it redirects to.</summary>
    public static async Task<Dictionary<string, string>> CallbackAsync(HttpClient client, FakeExternalIdentity identity)
    {
        (await client.PostAsJsonAsync("/test/external", identity)).EnsureSuccessStatusCode();
        var response = await client.GetAsync(new Uri("/api/auth/external/callback", UriKind.Relative));
        var location = response.Headers.Location?.ToString()
            ?? throw new InvalidOperationException($"No redirect: {response.StatusCode}");
        Assert.StartsWith($"{SiteUrl}/auth/external?", location, StringComparison.Ordinal);
        return QueryHelpers.ParseQuery(new Uri(location).Query).ToDictionary(p => p.Key, p => p.Value.ToString());
    }

    private sealed class FakeProviderStartup : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            // MapWhen, not Map: Map moves the path into PathBase, which would scope the cookie to it.
            app.MapWhen(c => c.Request.Path == "/test/external", branch => branch.Run(async context =>
            {
                var identity = await context.Request.ReadFromJsonAsync<FakeExternalIdentity>()
                    ?? throw new InvalidOperationException("Body required.");

                var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, identity.Key) };
                if (identity.Name is not null)
                {
                    claims.Add(new Claim(ClaimTypes.Name, identity.Name));
                }

                if (identity.VerifiedEmail is not null)
                {
                    claims.Add(new Claim(ExternalLogins.VerifiedEmailClaim, identity.VerifiedEmail));
                }

                if (identity.AccountName is not null)
                {
                    claims.Add(new Claim(ExternalLogins.AccountNameClaim, identity.AccountName));
                }

                var properties = new AuthenticationProperties();
                properties.Items["LoginProvider"] = identity.Scheme;
                properties.Items[ExternalChallenge.NonceItem] = identity.Nonce;
                if (identity.LinkTicket is not null)
                {
                    var codes = context.RequestServices.GetRequiredService<ExternalSignInCodes>();
                    properties.Items[ExternalChallenge.LinkUserItem] = codes.ReadLinkTicket(identity.LinkTicket)?.ToString("N");
                }

                await context.SignInAsync(
                    IdentityConstants.ExternalScheme,
                    new ClaimsPrincipal(new ClaimsIdentity(claims, identity.Scheme)),
                    properties);
            }));
            next(app);
        };
    }
}

/// <summary>A clock that starts at the real time and only moves when told to.</summary>
public sealed class TestClock : TimeProvider
{
    private TimeSpan _offset;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + _offset;

    public void Advance(TimeSpan by) => _offset += by;
}
