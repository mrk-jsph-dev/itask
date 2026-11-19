using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Common.Auth;

/// <summary>
/// Six-digit codes that confirm a new account's email. One code per user, stored hashed in
/// AspNetUserTokens with its expiry and failed attempts; a new code replaces the old one.
/// Sending is limited per user (<see cref="ResendCooldown"/>, <see cref="MaxSendsPerDay"/>) so the
/// form can't be used to flood an inbox.
/// </summary>
public sealed class EmailCodes(UserManager<AppUser> users, TimeProvider time)
{
    public const int Length = 6;

    public const int MaxAttempts = 5;

    public const int MaxSendsPerDay = 5;

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    private const string TokenProvider = "iTask";
    private const string TokenName = "EmailCode";

    private sealed record StoredCode(string Hash, DateTimeOffset ExpiresAt, int Attempts, DateTimeOffset SentAt, DateOnly Day, int SentThatDay);

    /// <summary>A new code for <paramref name="user"/>, or null while the cooldown or daily limit applies.</summary>
    public async Task<string?> IssueAsync(AppUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = time.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var previous = await ReadAsync(user);
        var sentToday = previous is not null && previous.Day == today ? previous.SentThatDay : 0;
        if (previous is not null && (now - previous.SentAt < ResendCooldown || sentToday >= MaxSendsPerDay))
        {
            return null;
        }

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var stored = new StoredCode(Hash(user.Id, code), now.Add(Lifetime), 0, now, today, sentToday + 1);
        var result = await users.SetAuthenticationTokenAsync(user, TokenProvider, TokenName, JsonSerializer.Serialize(stored));
        return result.Succeeded ? code : null;
    }

    /// <summary>
    /// True when <paramref name="code"/> matches the current, unexpired code; the code is then removed.
    /// A wrong code counts as an attempt; after <see cref="MaxAttempts"/> the code stops working.
    /// </summary>
    public async Task<bool> VerifyAsync(AppUser user, string code)
    {
        ArgumentNullException.ThrowIfNull(user);
        var stored = await ReadAsync(user);
        if (stored is null || stored.ExpiresAt <= time.GetUtcNow() || stored.Attempts >= MaxAttempts)
        {
            return false;
        }

        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(stored.Hash), Encoding.ASCII.GetBytes(Hash(user.Id, (code ?? "").Trim())));
        if (!matches)
        {
            await users.SetAuthenticationTokenAsync(
                user, TokenProvider, TokenName, JsonSerializer.Serialize(stored with { Attempts = stored.Attempts + 1 }));
            return false;
        }

        // Keep the send counters (cooldown, daily limit) but make the code unusable.
        await users.SetAuthenticationTokenAsync(
            user, TokenProvider, TokenName, JsonSerializer.Serialize(stored with { Attempts = MaxAttempts }));
        return true;
    }

    private async Task<StoredCode?> ReadAsync(AppUser user)
    {
        if (await users.GetAuthenticationTokenAsync(user, TokenProvider, TokenName) is not { } value)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StoredCode>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Hash(Guid userId, string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}\n{code}")));
}
