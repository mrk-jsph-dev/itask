using System.ComponentModel.DataAnnotations;
using ITask.Api.Common.Ordering;

namespace ITask.Api.Domain;

/// <summary>A column on a board. Names are unique among a board's active lists.</summary>
public class BoardList
{
    public const int NameMaxLength = 60;

    /// <summary>Most active lists a board can hold.</summary>
    public const int MaxActivePerBoard = 50;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BoardId { get; set; }

    public Board? Board { get; set; }

    public required string Name { get; set; }

    public string? Emoji { get; set; }

    /// <summary>Sort key within the board; see <see cref="PositionCalculator"/>.</summary>
    public double Position { get; set; }

    /// <summary>Soft cap on cards in the list; shown, not enforced.</summary>
    public int? WipLimit { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Optimistic concurrency token. SQL Server generates it; on SQLite the context stamps it.</summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    /// <summary>The lists every new board starts with, in order.</summary>
    public static readonly IReadOnlyList<(string Emoji, string Name)> Defaults =
    [
        ("📝", "To do"),
        ("🚧", "In progress"),
        ("👀", "Review"),
        ("✅", "Done"),
    ];

    public static IEnumerable<BoardList> CreateDefaults() =>
        Defaults.Select((d, i) => new BoardList
        {
            Name = d.Name,
            Emoji = d.Emoji,
            Position = PositionCalculator.Step * (i + 1),
        });
}
