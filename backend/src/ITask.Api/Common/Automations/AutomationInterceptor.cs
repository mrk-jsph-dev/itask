using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ITask.Api.Common.Automations;

/// <summary>
/// Runs <see cref="AutomationEngine"/> as part of every async save, then stamps
/// <see cref="Card.ListEnteredAt"/> on cards that are new or changed list. Registered before the
/// board events interceptor so the automations' activity goes out as live events too.
/// One instance per <see cref="DbContext"/> scope.
/// </summary>
internal sealed class AutomationInterceptor(AutomationEngine engine, AutomationRunLog log, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is AppDbContext db)
        {
            StampListEntered(db);
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is AppDbContext db)
        {
            await engine.RunAsync(db, cancellationToken);
            StampListEntered(db);
            db.PrepareForSave();
        }

        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        log.Commit();
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        log.Commit();
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => log.Discard();

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        log.Discard();
        return Task.CompletedTask;
    }

    private void StampListEntered(AppDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow();
        foreach (var entry in db.ChangeTracker.Entries<Card>())
        {
            if (entry.State == EntityState.Added
                || (entry.State == EntityState.Modified && entry.Property(c => c.ListId) is { IsModified: true } list
                    && list.OriginalValue != list.CurrentValue))
            {
                entry.Entity.ListEnteredAt = now;
            }
        }
    }
}
