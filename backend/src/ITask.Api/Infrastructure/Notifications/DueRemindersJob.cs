using ITask.Api.Common.Notifications;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Infrastructure.Notifications;

/// <summary>
/// Hourly: notifies about open cards due within <see cref="SoonWindow"/> (<c>due_soon</c>) and cards that
/// passed their due date in the last <see cref="OverdueWindow"/> (<c>overdue</c>). Recipients are the
/// assignees, or the creator when nobody is assigned. Each reminder is keyed by card and due date,
/// so reruns add nothing and moving the due date reminds again.
/// </summary>
internal sealed class DueRemindersJob(AppDbContext db) : IMaintenanceJob
{
    public static readonly TimeSpan SoonWindow = TimeSpan.FromHours(24);

    public static readonly TimeSpan OverdueWindow = TimeSpan.FromDays(7);

    public string Name => "due-reminders";

    public DateTimeOffset Slot(DateTimeOffset now) => MaintenanceRunner.LastHour(now);

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var from = now - OverdueWindow;
        var until = now + SoonWindow;

        var query = db.Cards.AsNoTracking().Where(c =>
            c.DueAt != null
            && c.CompletedAt == null
            && c.ArchivedAt == null
            && c.List!.ArchivedAt == null
            && c.Board!.ArchivedAt == null);

        // SQLite can't compare DateTimeOffset; there the window is applied in memory only.
        if (!db.Database.IsSqlite())
        {
            query = query.Where(c => c.DueAt > from && c.DueAt <= until);
        }

        var cards = (await query
                .Select(c => new
                {
                    c.Id,
                    c.BoardId,
                    c.Board!.KeyPrefix,
                    c.Number,
                    c.Title,
                    DueAt = c.DueAt!.Value,
                    c.CreatedById,
                    Assignees = c.Assignees.Select(a => a.UserId).ToList(),
                    CreatorIsMember = db.BoardMembers.Any(m => m.BoardId == c.BoardId && m.UserId == c.CreatedById),
                })
                .ToListAsync(ct))
            .Where(c => c.DueAt > from && c.DueAt <= until)
            .ToList();

        var wanted = new List<Notification>();
        foreach (var card in cards)
        {
            var type = card.DueAt <= now ? NotificationTypes.Overdue : NotificationTypes.DueSoon;
            var recipients = card.Assignees.Count > 0 ? card.Assignees : card.CreatorIsMember ? [card.CreatedById] : [];
            var data = NotificationData.WithCard(new { dueAt = card.DueAt }, Card.Key(card.KeyPrefix, card.Number), card.Title);
            wanted.AddRange(recipients.Select(userId => new Notification
            {
                UserId = userId,
                Type = type,
                BoardId = card.BoardId,
                CardId = card.Id,
                Data = data,
                Key = $"{type}:{card.Id:N}:{card.DueAt.UtcTicks}",
                CreatedAt = now,
            }));
        }

        if (wanted.Count == 0)
        {
            return;
        }

        var keys = wanted.Select(n => n.Key).Distinct().ToList();
        var existing = (await db.Notifications
                .Where(n => n.Key != null && keys.Contains(n.Key))
                .Select(n => new { n.UserId, n.Key })
                .ToListAsync(ct))
            .Select(n => (n.UserId, n.Key))
            .ToHashSet();

        db.Notifications.AddRange(wanted.Where(n => !existing.Contains((n.UserId, n.Key))));
        await db.SaveChangesAsync(ct);
    }
}
