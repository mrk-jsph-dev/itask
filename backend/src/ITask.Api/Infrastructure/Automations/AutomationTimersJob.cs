using ITask.Api.Common.Automations;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Maintenance;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Infrastructure.Automations;

/// <summary>
/// Hourly: runs automations with time-based triggers (due soon, overdue, stuck in a list, completed
/// a while ago) on the cards that match now. Each run is keyed by rule, card and the date it is
/// about, so reruns add nothing and moving the date runs it again. Runs act as the rule's creator;
/// a rule whose creator can no longer edit the board is turned off. Also drops old run history.
/// It runs inside the existing hourly slot, so it adds no wake-ups of its own.
/// </summary>
internal sealed partial class AutomationTimersJob(AppDbContext db, AutomationEngine engine, ILogger<AutomationTimersJob> logger) : IMaintenanceJob
{
    /// <summary>Most cards one rule acts on per hour; the rest wait for the next hour.</summary>
    public const int MaxCardsPerRule = 50;

    /// <summary>Run history is kept this long (Undo and run counts); keyed runs longer, so dedup holds.</summary>
    public static readonly TimeSpan RunRetention = TimeSpan.FromDays(30);

    public static readonly TimeSpan KeyedRunRetention = TimeSpan.FromDays(180);

    /// <summary>Overdue cards older than this are left alone, so a new rule doesn't sweep up years of backlog.</summary>
    public static readonly TimeSpan OverdueWindow = TimeSpan.FromDays(7);

    public string Name => "automation-timers";

    public DateTimeOffset Slot(DateTimeOffset now) => MaintenanceRunner.LastHour(now);

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var rules = (await db.AutomationRules.AsNoTracking()
                .Where(r => r.Enabled && r.Kind == AutomationKind.Rule && r.Board!.ArchivedAt == null)
                .ToListAsync(ct))
            .Select(r => (Rule: r, Spec: AutomationSpec.Of(r)))
            .Where(r => AutomationCatalog.IsHourly(r.Spec.Trigger.Type))
            .OrderBy(r => r.Rule.CreatedAt)
            .ToList();

        foreach (var (rule, spec) in rules)
        {
            await RunRuleAsync(rule, spec, now, ct);
        }

        await PruneAsync(now, ct);
    }

    private async Task RunRuleAsync(AutomationRule rule, AutomationSpec spec, DateTimeOffset now, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var board = await AutomationBoards.LoadAsync(db, rule.BoardId, ct);
        var creatorCanEdit = board.Members.TryGetValue(rule.CreatedById, out var creator) && creator.Role >= BoardRole.Editor;
        if (!creatorCanEdit || AutomationCatalog.Validate(rule.Kind, rule.ListId, rule.Direction, rule.Mode, spec, board).Count > 0)
        {
            await db.AutomationRules.Where(r => r.Id == rule.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Enabled, false), ct);
            LogDisabled(logger, rule.Id);
            return;
        }

        var due = await CandidatesAsync(rule.BoardId, spec.Trigger, now, ct);
        if (due.Count == 0)
        {
            return;
        }

        var keys = due.Select(d => d.Key).ToList();
        var done = (await db.AutomationRuns.AsNoTracking()
                .Where(r => r.RuleId == rule.Id && r.Key != null && keys.Contains(r.Key))
                .Select(r => r.Key!)
                .ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        var acted = 0;
        foreach (var (cardId, key) in due.Where(d => !done.Contains(d.Key)))
        {
            if (acted >= MaxCardsPerRule)
            {
                break;
            }

            db.ChangeTracker.Clear();
            var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == cardId && c.ArchivedAt == null, ct);
            if (card is null)
            {
                continue;
            }

            var state = await CardState.LoadAsync(db, card, ct);
            if (!spec.Conditions.All(state.Meets))
            {
                continue;
            }

            if (await engine.ApplyAsync(db, rule, spec.Actions, state, board, rule.CreatedById, key, ct) is null)
            {
                // Nothing to change; remember the occurrence so it isn't checked again every hour.
                db.AutomationRuns.Add(new AutomationRun
                {
                    RuleId = rule.Id,
                    BoardId = rule.BoardId,
                    CardId = card.Id,
                    TriggeredById = rule.CreatedById,
                    Key = key,
                    CreatedAt = now,
                    Outcome = AutomationOutcome.Skipped,
                });
            }

            try
            {
                await db.SaveChangesAsync(ct);
                acted++;
            }
            catch (DbUpdateException e)
            {
                LogSkipped(logger, e, rule.Id, card.Id);
            }
        }

        db.ChangeTracker.Clear();
    }

    /// <summary>Cards the trigger applies to right now, each with the key of the occurrence.</summary>
    private async Task<List<(Guid CardId, string Key)>> CandidatesAsync(Guid boardId, TriggerSpec trigger, DateTimeOffset now, CancellationToken ct)
    {
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

    private async Task PruneAsync(DateTimeOffset now, CancellationToken ct)
    {
        var plain = now - RunRetention;
        var keyed = now - KeyedRunRetention;
        if (db.Database.IsSqlite())
        {
            // CreatedAt is stored as UTC ticks on SQLite, which the query can compare.
            var plainTicks = plain.UtcTicks;
            var keyedTicks = keyed.UtcTicks;
            await db.Database.ExecuteSqlAsync(
                $"DELETE FROM AutomationRuns WHERE (\"Key\" IS NULL AND CreatedAt < {plainTicks}) OR CreatedAt < {keyedTicks}", ct);
            return;
        }

        await db.AutomationRuns.Where(r => (r.Key == null && r.CreatedAt < plain) || r.CreatedAt < keyed).ExecuteDeleteAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Automation {RuleId} turned off: its creator can't edit the board or it refers to something gone.")]
    private static partial void LogDisabled(ILogger logger, Guid ruleId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Automation {RuleId} skipped card {CardId} this hour.")]
    private static partial void LogSkipped(ILogger logger, Exception exception, Guid ruleId, Guid cardId);
}
