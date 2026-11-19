using System.Collections.Concurrent;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Realtime;
using ITask.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ITask.Api.Tests.Features.Realtime;

/// <summary>Keeps published events in memory instead of pushing them.</summary>
public sealed class RecordingBoardEvents : IBoardEvents
{
    public ConcurrentQueue<BoardEvent> Published { get; } = new();

    public Task PublishAsync(IReadOnlyList<BoardEvent> events, string? exceptConnectionId, CancellationToken ct)
    {
        foreach (var e in events)
        {
            Published.Enqueue(e);
        }

        return Task.CompletedTask;
    }

    public Task PresenceChangedAsync(Guid boardId, CancellationToken ct) => Task.CompletedTask;
}

public sealed class RecordingEventsApiFactory : ApiFactory
{
    public RecordingBoardEvents Events { get; } = new();

    protected override void ConfigureServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IBoardEvents>(Events));
}

public sealed class BoardEventsInterceptorTests(RecordingEventsApiFactory factory) : IClassFixture<RecordingEventsApiFactory>
{
    private async Task<(Guid BoardId, Guid UserId)> BoardAsync()
    {
        var owner = await factory.CreateSignedInClientAsync();
        return (await owner.CreateBoardAsync(), await owner.GetUserIdAsync());
    }

    private static void Record(AppDbContext db, Guid boardId, Guid userId) =>
        db.Activity.Add(new Domain.ActivityEntry { Type = ActivityTypes.BoardUpdated, BoardId = boardId, ActorId = userId });

    private int PublishedFor(Guid boardId) =>
        factory.Events.Published.Count(e => e.BoardId == boardId && e.Type == ActivityTypes.BoardUpdated);

    [Fact]
    public async Task Events_wait_for_the_transaction_to_commit()
    {
        var (boardId, userId) = await BoardAsync();

        await factory.WithDbAsync(async db =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            Record(db, boardId, userId);
            await db.SaveChangesAsync();
            Assert.Equal(0, PublishedFor(boardId));

            await transaction.CommitAsync();
            Assert.Equal(1, PublishedFor(boardId));
        });
    }

    [Fact]
    public async Task Rolled_back_changes_publish_nothing()
    {
        var (boardId, userId) = await BoardAsync();

        await factory.WithDbAsync(async db =>
        {
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                Record(db, boardId, userId);
                await db.SaveChangesAsync();
                await transaction.RollbackAsync();
            }

            // A later save must not flush what the rollback dropped.
            db.ChangeTracker.Clear();
            db.Activity.Add(new Domain.ActivityEntry { Type = ActivityTypes.BoardArchived, BoardId = boardId, ActorId = userId });
            await db.SaveChangesAsync();
        });

        Assert.Equal(0, PublishedFor(boardId));
        Assert.Contains(factory.Events.Published, e => e.BoardId == boardId && e.Type == ActivityTypes.BoardArchived);
    }

    [Fact]
    public async Task Saves_without_a_transaction_publish_at_once()
    {
        var (boardId, userId) = await BoardAsync();

        await factory.WithDbAsync(async db =>
        {
            Record(db, boardId, userId);
            await db.SaveChangesAsync();
        });

        Assert.Equal(1, PublishedFor(boardId));
    }
}
