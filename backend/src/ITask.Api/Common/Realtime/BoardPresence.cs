namespace ITask.Api.Common.Realtime;

/// <summary>
/// Which connection has which board open, in memory. Each connection has at most one board.
/// Accurate for a single API replica; with Azure SignalR across replicas each one sees only its own.
/// </summary>
public sealed class BoardPresence
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (Guid BoardId, PresenceUser User)> _connections = new(StringComparer.Ordinal);

    /// <summary>Opens <paramref name="boardId"/> on the connection; returns the board it had open before, if any.</summary>
    public Guid? Join(string connectionId, Guid boardId, PresenceUser user)
    {
        lock (_gate)
        {
            Guid? previous = _connections.TryGetValue(connectionId, out var entry) ? entry.BoardId : null;
            _connections[connectionId] = (boardId, user);
            return previous;
        }
    }

    /// <summary>Closes the connection's board; returns it, or null when none was open.</summary>
    public Guid? Leave(string connectionId)
    {
        lock (_gate)
        {
            return _connections.Remove(connectionId, out var entry) ? entry.BoardId : null;
        }
    }

    /// <summary>People on the board, once each however many tabs they have open.</summary>
    public IReadOnlyList<PresenceUser> UsersOn(Guid boardId)
    {
        lock (_gate)
        {
            return [.. _connections.Values
                .Where(c => c.BoardId == boardId)
                .Select(c => c.User)
                .DistinctBy(u => u.UserId)
                .OrderBy(u => u.DisplayName, StringComparer.CurrentCultureIgnoreCase)];
        }
    }

    public IReadOnlyList<string> ConnectionsOf(Guid boardId, Guid userId)
    {
        lock (_gate)
        {
            return [.. _connections
                .Where(c => c.Value.BoardId == boardId && c.Value.User.UserId == userId)
                .Select(c => c.Key)];
        }
    }
}
