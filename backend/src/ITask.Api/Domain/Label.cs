namespace ITask.Api.Domain;

/// <summary>A board-wide tag for cards. Names are unique per board.</summary>
public class Label
{
    public const int NameMaxLength = 30;

    public const int MaxPerBoard = 50;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BoardId { get; set; }

    public Board? Board { get; set; }

    public required string Name { get; set; }

    /// <summary>Hex colour (#RRGGBB).</summary>
    public required string Color { get; set; }

    public string? Emoji { get; set; }
}
