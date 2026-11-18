using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Cards;

/// <summary>Keeps a comment's mention rows in step with its body.</summary>
public static class CommentMentions
{
    /// <summary>Replaces <paramref name="comment"/>'s mentions with the board members its body names.</summary>
    public static async Task SyncAsync(AppDbContext db, Guid boardId, Comment comment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(comment);

        var members = await db.BoardMembers
            .Where(m => m.BoardId == boardId)
            .Select(m => new { m.UserId, m.User!.DisplayName })
            .ToListAsync(ct);
        var wanted = Mentions.Parse(comment.Body, members.Select(m => (m.UserId, m.DisplayName)));

        comment.Mentions.RemoveAll(m => !wanted.Contains(m.UserId));
        comment.Mentions.AddRange(wanted
            .Where(id => comment.Mentions.All(m => m.UserId != id))
            .Select(id => new CommentMention { CommentId = comment.Id, UserId = id }));
    }

    /// <summary>One comment as the API returns it.</summary>
    public static Task<CommentView> ViewAsync(AppDbContext db, Guid commentId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Comments.AsNoTracking().Where(c => c.Id == commentId).Select(CommentView.Project).FirstAsync(ct);
    }
}
