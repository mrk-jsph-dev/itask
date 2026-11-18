using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Auth;

/// <summary>
/// Resolves the current user's role on a board from <see cref="BoardMember"/>.
/// Returns null for non-members, including platform admins without a membership row.
/// </summary>
public interface IBoardAccess
{
    Task<BoardRole?> GetRoleAsync(Guid boardId, CancellationToken ct);

    Task<bool> HasAtLeastAsync(Guid boardId, BoardRole minimum, CancellationToken ct);
}

internal sealed class BoardAccess(AppDbContext db, ICurrentUser user) : IBoardAccess
{
    public async Task<BoardRole?> GetRoleAsync(Guid boardId, CancellationToken ct)
    {
        var userId = user.Id;
        var member = await db.BoardMembers
            .AsNoTracking()
            .Where(m => m.BoardId == boardId && m.UserId == userId)
            .Select(m => new { m.Role })
            .FirstOrDefaultAsync(ct);

        return member?.Role;
    }

    public async Task<bool> HasAtLeastAsync(Guid boardId, BoardRole minimum, CancellationToken ct)
    {
        var role = await GetRoleAsync(boardId, ct);
        return role is not null && role >= minimum;
    }
}
