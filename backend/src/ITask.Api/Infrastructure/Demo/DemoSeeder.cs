using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Demo;

/// <summary>
/// One demo account per role on a shared sample board:
/// Platform admin, board Owner, Editor and Viewer. Uses example.com addresses
/// (reserved, never real people). The nightly reset job arrives with the Deploy PR.
/// </summary>
public sealed partial class DemoSeeder(
    AppDbContext db,
    UserManager<AppUser> users,
    RoleManager<IdentityRole<Guid>> roles,
    IOptions<DemoOptions> options,
    ILogger<DemoSeeder> logger)
{
    private static readonly (string Email, string Name, BoardRole? Role, bool Admin)[] Accounts =
    [
        ("demo-admin@example.com", "Demo Admin", null, true),
        ("demo-owner@example.com", "Demo Owner", BoardRole.Owner, false),
        ("demo-editor@example.com", "Demo Editor", BoardRole.Editor, false),
        ("demo-viewer@example.com", "Demo Viewer", BoardRole.Viewer, false),
    ];

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

        const string boardName = "🚀 Demo: Website relaunch";
        var board = await db.Boards.Include(b => b.Members).FirstOrDefaultAsync(b => b.Name == boardName, ct);
        if (board is null)
        {
            board = new Board { Name = boardName, Emoji = "🚀" };
            db.Boards.Add(board);
        }

        foreach (var (email, name, role, admin) in Accounts)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name, IsDemo = true };
                var created = await users.CreateAsync(user, settings.Password);
                if (!created.Succeeded)
                {
                    LogSeedFailed(logger, email, string.Join("; ", created.Errors.Select(e => e.Code)));
                    continue;
                }
            }

            if (admin && !await users.IsInRoleAsync(user, PlatformRoles.PlatformAdmin))
            {
                await users.AddToRoleAsync(user, PlatformRoles.PlatformAdmin);
            }

            if (role is { } boardRole && board.Members.All(m => m.UserId != user.Id))
            {
                board.Members.Add(new BoardMember { UserId = user.Id, Role = boardRole });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Demo seeding is enabled but Demo:Password is not set; skipping.")]
    private static partial void LogMissingPassword(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not create demo account {Email}: {Errors}")]
    private static partial void LogSeedFailed(ILogger logger, string email, string errors);
}
