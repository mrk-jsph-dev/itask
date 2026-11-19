namespace ITask.Api.Domain;

/// <summary>A board layout a user saved; private to that user. <see cref="Content"/> is <c>TemplateContent</c> JSON.</summary>
public class BoardTemplate
{
    public const int NameMaxLength = 120;

    /// <summary>Most templates one user can keep.</summary>
    public const int MaxPerUser = 20;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OwnerId { get; set; }

    public required string Name { get; set; }

    public string? Emoji { get; set; }

    public string? Description { get; set; }

    /// <summary>Hex colour (#RRGGBB).</summary>
    public string Color { get; set; } = Board.DefaultColor;

    public required string Content { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
