using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Security;

/// <summary>
/// Per-day counters in the database (survive restarts and scale-to-zero) for app-wide caps:
/// sign-ups, AI calls, emails. <see cref="TryIncrementAsync"/> is safe under concurrent calls.
/// </summary>
public sealed class DailyCounters(AppDbContext db, TimeProvider time)
{
    public const string Signups = "signups";

    public const string Ai = "ai";

    public const string Email = "email";

    /// <summary>Adds one to today's <paramref name="name"/> unless it already reached <paramref name="limit"/>.</summary>
    public async Task<bool> TryIncrementAsync(string name, int limit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (limit <= 0)
        {
            return false;
        }

        var today = Today();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var updated = await db.UsageCounters
                .Where(c => c.Name == name && c.Date == today && c.Count < limit)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Count, c => c.Count + 1), ct);
            if (updated == 1)
            {
                return true;
            }

            if (await db.UsageCounters.AnyAsync(c => c.Name == name && c.Date == today, ct))
            {
                return false;
            }

            var counter = new UsageCounter { Name = name, Date = today, Count = 1 };
            db.UsageCounters.Add(counter);
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateException)
            {
                // Another request created today's row first; the next attempt counts against it.
            }
            finally
            {
                db.Entry(counter).State = EntityState.Detached;
            }
        }

        return false;
    }

    public async Task<int> CountAsync(string name, CancellationToken ct)
    {
        var today = Today();
        return await db.UsageCounters.Where(c => c.Name == name && c.Date == today)
            .Select(c => (int?)c.Count).FirstOrDefaultAsync(ct) ?? 0;
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
}
