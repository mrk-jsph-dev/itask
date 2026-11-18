using ITask.Api.Common.Auth;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Cards;

/// <summary>Access checks shared by the card and label write slices.</summary>
public static class CardGuard
{
    /// <summary>
    /// Null when the caller may change the board's cards; otherwise 404 for non-members,
    /// 403 below Editor, or 409 when the board is archived. All are ProblemDetails so slices can
    /// return them through one <see cref="ProblemHttpResult"/> case.
    /// </summary>
    public static async Task<ProblemHttpResult?> CheckEditorAsync(Guid boardId, AppDbContext db, IBoardAccess access, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(access);

        var role = await access.GetRoleAsync(boardId, ct);
        if (role is null)
        {
            return NotFound();
        }

        if (role < BoardRole.Editor)
        {
            return BoardResults.Forbidden();
        }

        if (await db.Boards.AnyAsync(b => b.Id == boardId && b.ArchivedAt != null, ct))
        {
            return BoardResults.Archived();
        }

        return null;
    }

    /// <summary>404 as ProblemDetails, for a board, list, card or label the caller can't see.</summary>
    public static ProblemHttpResult NotFound() => TypedResults.Problem(
        title: "Not found.",
        statusCode: StatusCodes.Status404NotFound);
}
