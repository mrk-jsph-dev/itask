using System.ComponentModel.DataAnnotations;

namespace ITask.Api.Domain;

public class Board
{
    public const string DefaultColor = "#1D5BF5";

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string? Emoji { get; set; }

    public string? Description { get; set; }

    /// <summary>Hex colour (#RRGGBB) for the board tile and header.</summary>
    public string Color { get; set; } = DefaultColor;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when the board is archived; archived boards are hidden from the default list.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Optimistic concurrency token. SQL Server generates it; on SQLite the context stamps it.</summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public List<BoardMember> Members { get; } = [];

    public List<BoardStar> Stars { get; } = [];

    public List<BoardInvite> Invites { get; } = [];

    public List<BoardList> Lists { get; } = [];
}
