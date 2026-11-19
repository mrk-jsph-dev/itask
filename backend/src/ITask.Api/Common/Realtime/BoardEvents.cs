using ITask.Api.Common.Activity;
using Microsoft.AspNetCore.SignalR;

namespace ITask.Api.Common.Realtime;

/// <summary>Pushes board events and presence to the board's live group.</summary>
public interface IBoardEvents
{
    /// <param name="exceptConnectionId">The acting tab, which already shows the change.</param>
    Task PublishAsync(IReadOnlyList<BoardEvent> events, string? exceptConnectionId, CancellationToken ct);

    Task PresenceChangedAsync(Guid boardId, CancellationToken ct);
}

internal sealed partial class BoardEvents(
    IHubContext<BoardHub> hub, BoardPresence presence, ILogger<BoardEvents> logger) : IBoardEvents
{
    /// <summary>Request header with the caller's hub connection id.</summary>
    public const string ConnectionHeader = "X-Live-Connection";

    public static string Group(Guid boardId) => $"board:{boardId:N}";

    public async Task PublishAsync(IReadOnlyList<BoardEvent> events, string? exceptConnectionId, CancellationToken ct)
    {
        foreach (var e in events)
        {
            try
            {
                if (e.Type is ActivityTypes.MemberRemoved or ActivityTypes.MemberLeft && e.UserId is { } userId)
                {
                    await DropAsync(e.BoardId, userId, ct);
                }

                var group = BoardEvents.Group(e.BoardId);
                var clients = exceptConnectionId is null
                    ? hub.Clients.Group(group)
                    : hub.Clients.GroupExcept(group, exceptConnectionId);
                await clients.SendAsync(BoardHub.EventMethod, e, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The change is saved; a missed push heals on the client's next resync.
                LogPublishFailed(logger, ex, e.Type);
            }
        }
    }

    public Task PresenceChangedAsync(Guid boardId, CancellationToken ct) =>
        hub.Clients.Group(Group(boardId))
            .SendAsync(BoardHub.PresenceMethod, new PresenceUpdate(boardId, presence.UsersOn(boardId)), ct);

    /// <summary>Takes a user who is no longer a member out of the board's group.</summary>
    private async Task DropAsync(Guid boardId, Guid userId, CancellationToken ct)
    {
        var connections = presence.ConnectionsOf(boardId, userId);
        if (connections.Count == 0)
        {
            return;
        }

        foreach (var connectionId in connections)
        {
            presence.Leave(connectionId);
            await hub.Groups.RemoveFromGroupAsync(connectionId, Group(boardId), ct);
        }

        await hub.Clients.Clients(connections).SendAsync(BoardHub.AccessLostMethod, new AccessLost(boardId), ct);
        await PresenceChangedAsync(boardId, ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't push live event {Type}.")]
    private static partial void LogPublishFailed(ILogger logger, Exception ex, string type);
}
