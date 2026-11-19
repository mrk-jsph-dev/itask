using ITask.Api.Common.Activity;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Persistence;
using ITask.Api.Infrastructure.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Infrastructure.Recurrence;

/// <summary>
/// Hourly: creates the next copy of every repeating card whose <c>NextRunAt</c> has passed. Cards that are
/// archived, or on an archived list or board, wait until restored. Each copy is saved on its own so one
/// busy board doesn't hold up the rest; a copy moves the rule forward, so reruns add nothing.
/// </summary>
internal sealed partial class RecurringCardsJob(AppDbContext db, IActivityRecorder activity, ILogger<RecurringCardsJob> logger) : IMaintenanceJob
{
    private const int MaxAttempts = 3;

    public string Name => "recurring-cards";

    public DateTimeOffset Slot(DateTimeOffset now) => MaintenanceRunner.LastHour(now);

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var due = await db.CardRecurrences
            .AsNoTracking()
            .Where(r => r.NextRunAt <= now
                && r.Card!.ArchivedAt == null
                && r.Card.List!.ArchivedAt == null
                && r.Card.Board!.ArchivedAt == null)
            .Select(r => r.Id)
            .ToListAsync(ct);

        foreach (var id in due)
        {
            for (var attempt = 1; ; attempt++)
            {
                db.ChangeTracker.Clear();
                var recurrence = await db.CardRecurrences.FirstAsync(r => r.Id == id, ct);
                if (recurrence.NextRunAt > now)
                {
                    break;
                }

                await Recurrences.SpawnNextAsync(db, activity.ForActor(recurrence.CreatedById), recurrence, now, ct);
                try
                {
                    await db.SaveChangesAsync(ct);
                    break;
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxAttempts)
                {
                }
                catch (DbUpdateConcurrencyException e)
                {
                    LogBusy(logger, e, id);
                    break;
                }
            }
        }

        db.ChangeTracker.Clear();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Recurring card {RecurrenceId} skipped: the board stayed busy; retrying next hour.")]
    private static partial void LogBusy(ILogger logger, Exception exception, Guid recurrenceId);
}
