using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ITask.Api.Common.Auth;

public sealed class PlatformAdminOptions
{
    public const string Section = "Admin";

    /// <summary>
    /// Emails (separated by <c>;</c> or <c>,</c>) that become Platform admins at startup and when
    /// they register. Listing only grants: removing an email later does not demote the account.
    /// </summary>
    public string? Emails { get; set; }

    public IReadOnlySet<string> EmailSet => (Emails ?? string.Empty)
        .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Grants and removes the Platform admin role: from <see cref="PlatformAdminOptions.Emails"/> at
/// startup and registration, and from the admin Users page.
/// </summary>
public sealed partial class PlatformAdmins(
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<PlatformAdminOptions> options,
    ILogger<PlatformAdmins> logger)
{
    public async Task EnsureRoleAsync()
    {
        if (!await roles.RoleExistsAsync(PlatformRoles.PlatformAdmin))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(PlatformRoles.PlatformAdmin));
        }
    }

    /// <summary>Grants the role to every listed email that already has an account.</summary>
    public async Task GrantListedAsync()
    {
        await EnsureRoleAsync();
        foreach (var email in options.Value.EmailSet)
        {
            if (await users.FindByEmailAsync(email) is { } user)
            {
                await GrantAsync(user);
            }
        }
    }

    /// <summary>Grants the role when <paramref name="user"/>'s email is listed.</summary>
    public async Task GrantIfListedAsync(AppUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user.Email is { } email && options.Value.EmailSet.Contains(email))
        {
            await EnsureRoleAsync();
            await GrantAsync(user);
        }
    }

    /// <summary>Adds or removes the role for <paramref name="user"/>, on behalf of <paramref name="actorId"/>.</summary>
    public async Task SetAsync(AppUser user, bool admin, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (admin)
        {
            await EnsureRoleAsync();
            await users.AddToRoleAsync(user, PlatformRoles.PlatformAdmin);
        }
        else
        {
            await users.RemoveFromRoleAsync(user, PlatformRoles.PlatformAdmin);
        }

        LogChanged(logger, actorId, user.Id, admin ? "Platform admin" : "User");
    }

    private async Task GrantAsync(AppUser user)
    {
        if (user.IsDemo || await users.IsInRoleAsync(user, PlatformRoles.PlatformAdmin))
        {
            return;
        }

        await users.AddToRoleAsync(user, PlatformRoles.PlatformAdmin);
        LogGranted(logger, user.Id);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Granted Platform admin to user {UserId} from Admin:Emails.")]
    private static partial void LogGranted(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Platform admin {ActorId} set user {UserId} to {Type}.")]
    private static partial void LogChanged(ILogger logger, Guid actorId, Guid userId, string type);
}
