using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Demo;

/// <summary>
/// Idempotently creates the <see cref="DemoAccounts"/> (Platform admin, board Owner, Editor, Viewer),
/// the teammates who fill the boards (<see cref="DemoBoards.People"/>, no sign-in) and, when the demo
/// owner has no boards, the demo workspace (<see cref="DemoBoards"/>). Runs only when
/// <c>Demo:Enabled</c> is true and <c>Demo:Password</c> is set.
/// </summary>
public sealed partial class DemoSeeder(
    AppDbContext db,
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<DemoOptions> options,
    TimeProvider clock,
    ILogger<DemoSeeder> logger)
{
    /// <summary>Avatar colours of the sign-in accounts, so they are told apart on boards.</summary>
    private static readonly Dictionary<string, string> AccountColors = new(StringComparer.Ordinal)
    {
        ["admin"] = "#111827",
        ["owner"] = "#1D5BF5",
        ["editor"] = "#0F766E",
        ["viewer"] = "#9333EA",
    };

    public async Task SeedAsync(CancellationToken ct)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.Password))
        {
            LogMissingPassword(logger);
            return;
        }

        if (!await roles.RoleExistsAsync(PlatformRoles.PlatformAdmin))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(PlatformRoles.PlatformAdmin));
        }

        var people = new Dictionary<string, AppUser>(StringComparer.Ordinal);
        foreach (var account in DemoAccounts.All)
        {
            var user = await EnsureUserAsync(account.Email, account.Name, AccountColors[account.Key], settings.Password);
            if (user is null)
            {
                continue;
            }

            if (account.PlatformAdmin && !await users.IsInRoleAsync(user, PlatformRoles.PlatformAdmin))
            {
                await users.AddToRoleAsync(user, PlatformRoles.PlatformAdmin);
            }

            people[account.Key] = user;
        }

        foreach (var person in DemoBoards.People)
        {
            if (await EnsureUserAsync($"demo-{person.Key}@example.com", person.Name, person.Color, password: null) is { } user)
            {
                people[person.Key] = user;
            }
        }

        var needed = DemoBoards.Data.Boards.SelectMany(b => b.Members.Keys).Distinct();
        if (needed.All(people.ContainsKey)
            && !await db.BoardMembers.AnyAsync(m => m.UserId == people["owner"].Id, ct))
        {
            DemoBoards.Add(db, people, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Finds or creates a demo user; teammates get no password, so they can't sign in.</summary>
    private async Task<AppUser?> EnsureUserAsync(string email, string name, string color, string? password)
    {
        if (await users.FindByEmailAsync(email) is { } existing)
        {
            return existing;
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = name,
            AvatarColor = color,
            IsDemo = true,
        };
        var created = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
        if (created.Succeeded)
        {
            return user;
        }

        LogSeedFailed(logger, email, string.Join("; ", created.Errors.Select(e => e.Code)));
        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo seeding is enabled but Demo:Password is not set; skipping.")]
    private static partial void LogMissingPassword(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not create demo account {Email}: {Errors}")]
    private static partial void LogSeedFailed(ILogger logger, string email, string errors);
}
