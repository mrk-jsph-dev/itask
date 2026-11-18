using System.Linq.Expressions;
using ITask.Api.Domain;

namespace ITask.Api.Common.Cards;

public sealed record ChecklistItemView(
    Guid Id, string Text, bool IsDone, double Position, DateTimeOffset? DueAt, Guid? AssigneeId)
{
    public static readonly Expression<Func<ChecklistItem, ChecklistItemView>> Project =
        i => new ChecklistItemView(i.Id, i.Text, i.IsDone, i.Position, i.DueAt, i.AssigneeId);

    public static ChecklistItemView From(ChecklistItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new ChecklistItemView(item.Id, item.Text, item.IsDone, item.Position, item.DueAt, item.AssigneeId);
    }
}

public sealed record AttachmentView(
    Guid Id,
    string FileName,
    string ContentType,
    long SizeBytes,
    Guid UploadedById,
    string? UploadedByName,
    DateTimeOffset CreatedAt)
{
    public static readonly Expression<Func<Attachment, AttachmentView>> Project = a => new AttachmentView(
        a.Id, a.FileName, a.ContentType, a.SizeBytes, a.UploadedById, a.UploadedBy!.DisplayName, a.CreatedAt);
}

public sealed record MentionView(Guid UserId, string DisplayName);

public sealed record CommentView(
    Guid Id,
    Guid AuthorId,
    string AuthorName,
    string AuthorColor,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EditedAt,
    IReadOnlyList<MentionView> Mentions)
{
    public static readonly Expression<Func<Comment, CommentView>> Project = c => new CommentView(
        c.Id,
        c.AuthorId,
        c.Author!.DisplayName,
        c.Author.AvatarColor,
        c.Body,
        c.CreatedAt,
        c.EditedAt,
        c.Mentions.Select(m => new MentionView(m.UserId, m.User!.DisplayName)).ToList());
}
