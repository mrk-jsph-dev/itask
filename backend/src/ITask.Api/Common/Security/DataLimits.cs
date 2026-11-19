using ITask.Api.Common.Auth;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Security;

/// <summary>Caps on how much one account can create, so the database stays within the free tier.</summary>
public static class DataLimits
{
    /// <summary>409 when the user already owns <see cref="Board.MaxOwnedPerUser"/> boards; otherwise null.</summary>
    public static async Task<ProblemHttpResult?> CheckOwnedBoardsAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var owned = await db.BoardMembers.CountAsync(m => m.UserId == userId && m.Role == BoardRole.Owner, ct);
        return owned >= Board.MaxOwnedPerUser
            ? BoardResults.Conflict($"You can own up to {Board.MaxOwnedPerUser} boards. Delete one you no longer need first.")
            : null;
    }

    /// <summary>409 when the board already holds <see cref="Card.MaxPerBoard"/> cards; otherwise null.</summary>
    public static async Task<ProblemHttpResult?> CheckCardsAsync(AppDbContext db, Guid boardId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.Cards.CountAsync(c => c.BoardId == boardId, ct) >= Card.MaxPerBoard
            ? BoardResults.Conflict($"A board can hold up to {Card.MaxPerBoard} cards. Delete archived cards or start a new board.")
            : null;
    }
}
