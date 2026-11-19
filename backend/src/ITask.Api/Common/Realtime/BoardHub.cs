using ITask.Api.Common.Auth;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Realtime;

/// <summary>
/// Live board updates. A client calls <see cref="Join"/> with the board it shows and then receives
/// <c>event</c>, <c>presence</c> and <c>accessLost</c> messages for it. Non-members get the same
/// "not found" as the API's 404.
/// </summary>
[Authorize]
internal sealed class BoardHub(IBoardAccess access, AppDbContext db, BoardPresence presence, IBoardEvents events) : Hub
{
    public const string Path = "/api/hubs/board";
    public const string EventMethod = "event";
    public const string PresenceMethod = "presence";
    public const string AccessLostMethod = "accessLost";

    public async Task<IReadOnlyList<PresenceUser>> Join(Guid boardId)
    {
        var ct = Context.ConnectionAborted;
        var userId = UserId();
        if (await access.GetRoleAsync(boardId, userId, ct) is null)
        {
            throw new HubException("Board not found.");
        }

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new PresenceUser(u.Id, u.DisplayName, u.AvatarColor))
            .FirstAsync(ct);

        var previous = presence.Join(Context.ConnectionId, boardId, user);
        if (previous is { } old && old != boardId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, BoardEvents.Group(old), ct);
            await events.PresenceChangedAsync(old, ct);
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, BoardEvents.Group(boardId), ct);
        await events.PresenceChangedAsync(boardId, ct);
        return presence.UsersOn(boardId);
    }

    public async Task Leave()
    {
        if (presence.Leave(Context.ConnectionId) is { } boardId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, BoardEvents.Group(boardId));
            await events.PresenceChangedAsync(boardId, CancellationToken.None);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (presence.Leave(Context.ConnectionId) is { } boardId)
        {
            await events.PresenceChangedAsync(boardId, CancellationToken.None);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Guid UserId() =>
        Guid.TryParse(Context.UserIdentifier, out var id) ? id : throw new HubException("Not signed in.");
}
