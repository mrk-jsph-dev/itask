using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Common.Auth;

/// <summary>An external login waiting to be added once its owner confirms it from a signed-in session.</summary>
public sealed record PendingLogin(string Scheme, string Key, string? DisplayName);

public sealed record RedeemedCode(AppUser User, PendingLogin? Link);

/// <summary>
/// One-time codes that carry an external sign-in from the OAuth callback (a top-level page on the API's
/// site) to the SPA, which redeems the code with <c>fetch</c> so the auth cookie is set in the SPA's
/// cookie partition. The code is stored hashed together with the browser's nonce, in AspNetUserTokens,
/// one per user; redeeming removes it (the user's concurrency stamp makes that single use).
/// Link tickets name the signed-in user who starts linking a provider.
/// </summary>
public sealed class ExternalSignInCodes(
    UserManager<AppUser> users, IDataProtectionProvider dataProtection, TimeProvider time)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan LinkTicketLifetime = TimeSpan.FromMinutes(5);

    private const string TokenProvider = "iTask";
    private const string TokenName = "ExternalSignIn";

    private sealed record StoredCode(string Hash, DateTimeOffset ExpiresAt, PendingLogin? Link);

    /// <summary>Returns <c>{userId:N}.{secret}</c>, or null when the token could not be saved.</summary>
    public async Task<string?> IssueAsync(AppUser user, string nonce, PendingLogin? link)
    {
        ArgumentNullException.ThrowIfNull(user);
        var secret = InviteTokens.Create();
        var stored = new StoredCode(Hash(secret, nonce), time.GetUtcNow().Add(CodeLifetime), link);
        var result = await users.SetAuthenticationTokenAsync(user, TokenProvider, TokenName, JsonSerializer.Serialize(stored));
        return result.Succeeded ? $"{user.Id:N}.{secret}" : null;
    }

    /// <summary>Null when the code is unknown, expired, already used or was issued with another nonce.</summary>
    public async Task<RedeemedCode?> RedeemAsync(string code, string nonce)
    {
        ArgumentNullException.ThrowIfNull(code);
        var parts = code.Split('.', 2);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out var userId))
        {
            return null;
        }

        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null || await users.GetAuthenticationTokenAsync(user, TokenProvider, TokenName) is not { } value)
        {
            return null;
        }

        StoredCode? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredCode>(value);
        }
        catch (JsonException)
        {
            return null;
        }

        if (stored is null
            || stored.ExpiresAt <= time.GetUtcNow()
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(stored.Hash), Encoding.ASCII.GetBytes(Hash(parts[1], nonce))))
        {
            return null;
        }

        var removed = await users.RemoveAuthenticationTokenAsync(user, TokenProvider, TokenName);
        return removed.Succeeded ? new RedeemedCode(user, stored.Link) : null;
    }

    public string CreateLinkTicket(Guid userId) => LinkProtector.Protect(userId.ToString("N"), LinkTicketLifetime);

    /// <summary>The user id inside a valid, unexpired ticket.</summary>
    public Guid? ReadLinkTicket(string ticket)
    {
        try
        {
            return Guid.TryParseExact(LinkProtector.Unprotect(ticket), "N", out var id) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private ITimeLimitedDataProtector LinkProtector =>
        dataProtection.CreateProtector("ITask.ExternalLink").ToTimeLimitedDataProtector();

    private static string Hash(string secret, string nonce) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{secret}\n{nonce}")));
}
