using System.ComponentModel.DataAnnotations;

namespace ITask.Api.Domain;

public class Board
{
    public const string DefaultColor = "#1D5BF5";

    /// <summary>Most boards one user can own (archived ones included).</summary>
    public const int MaxOwnedPerUser = 50;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    /// <summary>Lower-cased <see cref="Name"/> for search; set by <c>AppDbContext</c> on save.</summary>
    public string SearchName { get; set; } = "";

    public string? Emoji { get; set; }

    public string? Description { get; set; }

    /// <summary>Hex colour (#RRGGBB) for the board tile and header.</summary>
    public string Color { get; set; } = DefaultColor;

    /// <summary>Prefix of card keys on this board, e.g. "ITK" in ITK-42. See <see cref="CardKeys"/>.</summary>
    public string KeyPrefix { get; set; } = CardKeys.Fallback;

    /// <summary>Number the next card gets. Bumped with the board's row version, so concurrent creates can't share one.</summary>
    public int NextCardNumber { get; set; } = 1;

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

    public List<Card> Cards { get; } = [];

    public List<Label> Labels { get; } = [];
}
