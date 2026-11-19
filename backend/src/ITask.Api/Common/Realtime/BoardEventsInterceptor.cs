using System.Data.Common;
using System.Text.Json;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ITask.Api.Common.Realtime;

/// <summary>
/// Turns the activity entries a save adds into <see cref="BoardEvent"/>s and publishes them once
/// they are committed: right after the save, or when an explicit transaction commits (dropped on
/// rollback). One instance per <see cref="DbContext"/> scope.
/// </summary>
internal sealed class BoardEventsInterceptor(IBoardEvents events, IHttpContextAccessor http)
    : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private List<ActivityEntry> _saving = [];
    private readonly List<BoardEvent> _pending = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (Collect(eventData.Context))
        {
            FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (Collect(eventData.Context))
        {
            await FlushAsync(cancellationToken);
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => _saving = [];

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _saving = [];
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        FlushAsync(CancellationToken.None).GetAwaiter().GetResult();

    public Task TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
        FlushAsync(cancellationToken);

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => _pending.Clear();

    public Task TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        _pending.Clear();
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context) =>
        _saving = context is null
            ? []
            : [.. context.ChangeTracker.Entries<ActivityEntry>().Where(e => e.State == EntityState.Added).Select(e => e.Entity)];

    /// <summary>Queues the saved entries' events; true when they can go out now (no open transaction).</summary>
    private bool Collect(DbContext? context)
    {
        if (context is null || _saving.Count == 0)
        {
            return false;
        }

        foreach (var entry in _saving)
        {
            var card = entry.CardId is { } cardId
                ? context.ChangeTracker.Entries<Card>().FirstOrDefault(c => c.Entity.Id == cardId)?.Entity
                : null;
            _pending.Add(new BoardEvent(
                entry.Type, entry.BoardId, entry.CardId, card?.ListId, card?.Position,
                MemberId(entry), entry.ActorId, entry.CreatedAt));
        }

        _saving = [];
        return context.Database.CurrentTransaction is null;
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        if (_pending.Count == 0)
        {
            return;
        }

        BoardEvent[] batch = [.. _pending];
        _pending.Clear();
        var except = http.HttpContext?.Request.Headers[BoardEvents.ConnectionHeader].ToString();
        await events.PublishAsync(batch, string.IsNullOrEmpty(except) ? null : except, ct);
    }

    /// <summary><c>member.*</c> entries record <c>member: { id, name }</c>.</summary>
    private static Guid? MemberId(ActivityEntry entry)
    {
        if (!entry.Type.StartsWith("member.", StringComparison.Ordinal))
        {
            return null;
        }

        using var data = JsonDocument.Parse(entry.Data);
        return data.RootElement.TryGetProperty("member", out var member)
            && member.TryGetProperty("id", out var id)
            && id.TryGetGuid(out var memberId)
                ? memberId
                : null;
    }
}
