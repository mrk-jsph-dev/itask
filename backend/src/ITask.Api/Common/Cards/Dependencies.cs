using ITask.Api.Common.Activity;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Notifications;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Cards;

/// <summary>A blocker as the card sheet lists it.</summary>
public sealed record BlockerView(Guid Id, string Key, string Title, bool Completed, bool Archived);

/// <summary>Rules for "blocked by" links between cards of one board.</summary>
public static class Dependencies
{
    /// <summary>True when <paramref name="blockerId"/> already waits on <paramref name="cardId"/>, directly or through other cards.</summary>
    public static async Task<bool> WouldCycleAsync(AppDbContext db, Guid boardId, Guid cardId, Guid blockerId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var edges = (await db.CardDependencies.AsNoTracking()
                .Where(d => d.Card!.BoardId == boardId)
                .Select(d => new { d.CardId, d.BlockedById })
                .ToListAsync(ct))
            .ToLookup(d => d.CardId, d => d.BlockedById);

        var seen = new HashSet<Guid> { blockerId };
        var queue = new Queue<Guid>([blockerId]);
        while (queue.TryDequeue(out var next))
        {
            if (next == cardId)
            {
                return true;
            }

            foreach (var waitsOn in edges[next])
            {
                if (seen.Add(waitsOn))
                {
                    queue.Enqueue(waitsOn);
                }
            }
        }

        return false;
    }

    /// <summary>409 when the card already has <see cref="CardDependency.MaxBlockersPerCard"/> blockers; otherwise null.</summary>
    public static async Task<ProblemHttpResult?> CheckLimitAsync(AppDbContext db, Guid cardId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.CardDependencies.CountAsync(d => d.CardId == cardId, ct) >= CardDependency.MaxBlockersPerCard
            ? BoardResults.Conflict($"A card can wait on up to {CardDependency.MaxBlockersPerCard} cards.")
            : null;
    }

    /// <summary>409 with <c>code: "dependency_cycle"</c>.</summary>
    public static ProblemHttpResult Cycle() => TypedResults.Problem(
        title: "That would make a loop: the other card already waits on this one.",
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["code"] = "dependency_cycle" });

    /// <summary>The card's blockers, open ones first, then by key number.</summary>
    public static async Task<List<BlockerView>> BlockersAsync(AppDbContext db, Guid boardId, Guid cardId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var prefix = await db.Boards.Where(b => b.Id == boardId).Select(b => b.KeyPrefix).FirstAsync(ct);
        var rows = await db.CardDependencies.AsNoTracking()
            .Where(d => d.CardId == cardId)
            .Select(d => new { d.BlockedBy!.Id, d.BlockedBy.Number, d.BlockedBy.Title, d.BlockedBy.CompletedAt, d.BlockedBy.ArchivedAt })
            .ToListAsync(ct);
        return [.. rows
            .OrderBy(r => r.CompletedAt is not null || r.ArchivedAt is not null)
            .ThenBy(r => r.Number)
            .Select(r => new BlockerView(r.Id, Card.Key(prefix, r.Number), r.Title, r.CompletedAt is not null, r.ArchivedAt is not null))];
    }

    /// <summary>
    /// Adds <c>card.unblocked</c> activity and notifications for cards whose last open blocker is completed or
    /// archived by the pending changes. Entries in <paramref name="handled"/> are skipped and added to it,
    /// so a save can run this before and after its automations. Nothing here saves.
    /// </summary>
    public static async Task RecordUnblockedAsync(AppDbContext db, HashSet<ActivityEntry> handled, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(handled);

        db.ChangeTracker.DetectChanges();
        var closing = db.ChangeTracker.Entries<ActivityEntry>()
            .Where(e => e.State == EntityState.Added
                && e.Entity.CardId is not null
                && e.Entity.Type is ActivityTypes.CardCompleted or ActivityTypes.CardArchived)
            .Select(e => e.Entity)
            .Where(handled.Add)
            .ToList();

        // Only cards that go from open to closed in this save can unblock anything.
        var closed = new Dictionary<Guid, ActivityEntry>();
        foreach (var entry in closing)
        {
            if (db.Cards.Local.FirstOrDefault(c => c.Id == entry.CardId) is { } card
                && db.Entry(card) is var tracked
                && (card.CompletedAt is not null || card.ArchivedAt is not null)
                && tracked.State != EntityState.Added
                && tracked.Property(c => c.CompletedAt).OriginalValue is null
                && tracked.Property(c => c.ArchivedAt).OriginalValue is null)
            {
                closed.TryAdd(card.Id, entry);
            }
        }

        if (closed.Count == 0)
        {
            return;
        }

        var closedIds = closed.Keys.ToList();
        var dependentIds = await db.CardDependencies.AsNoTracking()
            .Where(d => closedIds.Contains(d.BlockedById))
            .Select(d => d.CardId)
            .Distinct()
            .ToListAsync(ct);
        if (dependentIds.Count == 0)
        {
            return;
        }

        var links = await db.CardDependencies.AsNoTracking()
            .Where(d => dependentIds.Contains(d.CardId))
            .Select(d => new { d.CardId, d.BlockedById, d.BlockedBy!.CompletedAt, d.BlockedBy.ArchivedAt })
            .ToListAsync(ct);
        var dependents = await db.Cards.AsNoTracking()
            .Where(c => dependentIds.Contains(c.Id))
            .Select(c => new
            {
                c.Id,
                c.BoardId,
                c.Number,
                c.Title,
                c.CompletedAt,
                c.ArchivedAt,
                c.Board!.KeyPrefix,
                Assignees = c.Assignees.Select(a => a.UserId).ToList(),
            })
            .ToListAsync(ct);

        bool IsOpen(Guid id, DateTimeOffset? completedAt, DateTimeOffset? archivedAt) =>
            db.Cards.Local.FirstOrDefault(c => c.Id == id) is { } local
                ? local.CompletedAt is null && local.ArchivedAt is null
                : completedAt is null && archivedAt is null;

        foreach (var dependent in dependents)
        {
            if (!IsOpen(dependent.Id, dependent.CompletedAt, dependent.ArchivedAt))
            {
                continue;
            }

            var blockers = links.Where(l => l.CardId == dependent.Id).ToList();
            if (blockers.Any(l => IsOpen(l.BlockedById, l.CompletedAt, l.ArchivedAt)))
            {
                continue;
            }

            var cause = blockers.Select(l => closed.GetValueOrDefault(l.BlockedById)).First(e => e is not null)!;
            var blocker = db.Cards.Local.First(c => c.Id == cause.CardId);
            var blockerRef = new { key = Card.Key(dependent.KeyPrefix, blocker.Number), title = blocker.Title };
            var key = Card.Key(dependent.KeyPrefix, dependent.Number);

            db.Activity.Add(new ActivityEntry
            {
                Type = ActivityTypes.CardUnblocked,
                BoardId = dependent.BoardId,
                CardId = dependent.Id,
                ActorId = cause.ActorId,
                Data = NotificationData.WithCard(new { blocker = blockerRef }, key, dependent.Title),
                CreatedAt = now,
            });

            var data = NotificationData.WithCard(new { blocker = blockerRef }, key, dependent.Title);
            foreach (var userId in dependent.Assignees.Where(id => id != cause.ActorId))
            {
                db.Notifications.Add(new Notification
                {
                    UserId = userId,
                    Type = NotificationTypes.Unblocked,
                    BoardId = dependent.BoardId,
                    CardId = dependent.Id,
                    ActorId = cause.ActorId,
                    Data = data,
                    CreatedAt = now,
                });
            }
        }
    }
}
