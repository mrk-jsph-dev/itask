using ITask.Api.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Automations;

/// <summary>Which cards a time-based trigger applies to, shared by the hourly job and the preview.</summary>
public static class AutomationSchedule
{
    /// <summary>Overdue cards older than this are left alone, so a new rule doesn't sweep up years of backlog.</summary>
    public static readonly TimeSpan OverdueWindow = TimeSpan.FromDays(7);

    /// <summary>Cards a time-based trigger applies to right now, each with the key of the occurrence.</summary>
    public static async Task<List<(Guid CardId, string Key)>> CandidatesAsync(AppDbContext db, Guid boardId, TriggerSpec trigger, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(trigger);
        var cards = db.Cards.AsNoTracking().Where(c => c.BoardId == boardId && c.ArchivedAt == null && c.List!.ArchivedAt == null);

        // SQLite can't compare DateTimeOffset in queries, so the dates are checked in memory (a board holds at most 2,000 cards).
        switch (trigger.Type)
        {
            case AutomationCatalog.DueSoon:
            {
                var until = now.AddDays(trigger.Days ?? 1);
                return (await cards.Where(c => c.DueAt != null && c.CompletedAt == null).Select(c => new { c.Id, c.DueAt }).ToListAsync(ct))
                    .Where(c => c.DueAt > now && c.DueAt <= until)
                    .Select(c => (c.Id, $"{trigger.Type}:{c.DueAt!.Value.UtcTicks}"))
                    .ToList();
            }

            case AutomationCatalog.DueOverdue:
            {
                var from = now - OverdueWindow;
                return (await cards.Where(c => c.DueAt != null && c.CompletedAt == null).Select(c => new { c.Id, c.DueAt }).ToListAsync(ct))
                    .Where(c => c.DueAt <= now && c.DueAt > from)
                    .Select(c => (c.Id, $"{trigger.Type}:{c.DueAt!.Value.UtcTicks}"))
                    .ToList();
            }

            case AutomationCatalog.ListStale:
            {
                var before = now.AddDays(-(trigger.Days ?? 1));
                return (await cards.Where(c => c.ListId == trigger.ListId && c.CompletedAt == null)
                        .Select(c => new { c.Id, c.ListEnteredAt })
                        .ToListAsync(ct))
                    .Where(c => c.ListEnteredAt <= before)
                    .Select(c => (c.Id, $"{trigger.Type}:{c.ListEnteredAt.UtcTicks}"))
                    .ToList();
            }

            case AutomationCatalog.CompletedAged:
            {
                var before = now.AddDays(-(trigger.Days ?? 1));
                return (await cards.Where(c => c.CompletedAt != null).Select(c => new { c.Id, c.CompletedAt }).ToListAsync(ct))
                    .Where(c => c.CompletedAt <= before)
                    .Select(c => (c.Id, $"{trigger.Type}:{c.CompletedAt!.Value.UtcTicks}"))
                    .ToList();
            }

            default:
                return [];
        }
    }
}
