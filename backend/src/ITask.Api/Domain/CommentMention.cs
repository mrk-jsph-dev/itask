namespace ITask.Api.Domain;

/// <summary>A board member named with <c>@DisplayName</c> in a comment.</summary>
public class CommentMention
{
    public Guid CommentId { get; set; }

    public Comment? Comment { get; set; }

    public Guid UserId { get; set; }

    public AppUser? User { get; set; }
}
