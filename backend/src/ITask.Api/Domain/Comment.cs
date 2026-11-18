namespace ITask.Api.Domain;

/// <summary>A markdown comment on a card.</summary>
public class Comment
{
    public const int BodyMaxLength = 5_000;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid CardId { get; set; }

    public Card? Card { get; set; }

    public Guid AuthorId { get; set; }

    public AppUser? Author { get; set; }

    /// <summary>Markdown.</summary>
    public required string Body { get; set; }

    /// <summary>Lower-cased <see cref="Body"/> for search; set by <c>AppDbContext</c> on save.</summary>
    public string SearchBody { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? EditedAt { get; set; }

    public List<CommentMention> Mentions { get; } = [];
}
