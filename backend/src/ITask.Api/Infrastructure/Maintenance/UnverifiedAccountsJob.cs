using ITask.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Infrastructure.Maintenance;

/// <summary>
/// Deletes accounts whose email was never confirmed within <see cref="MaxAge"/>: they can't sign in, so
/// they are left-over or scripted sign-ups. Demo accounts, accounts with an external login and anyone
/// on a board are kept.
/// </summary>
internal sealed class UnverifiedAccountsJob(AppDbContext db) : IMaintenanceJob
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    public string Name => "unverified-accounts";

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Few rows (sign-ups are capped per day); CreatedAt is compared in memory because SQLite can't.
        var candidates = await db.Users
            .Where(u => !u.EmailConfirmed && !u.IsDemo
                && !db.UserLogins.Any(l => l.UserId == u.Id)
                && !db.BoardMembers.Any(m => m.UserId == u.Id))
            .Select(u => new { u.Id, u.CreatedAt })
            .ToListAsync(ct);
        var stale = candidates.Where(u => u.CreatedAt < now - MaxAge).Select(u => u.Id).ToList();
        if (stale.Count > 0)
        {
            await db.Users.Where(u => stale.Contains(u.Id)).ExecuteDeleteAsync(ct);
        }
    }
}
