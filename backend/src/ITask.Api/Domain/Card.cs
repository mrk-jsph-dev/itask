using System.ComponentModel.DataAnnotations;
using ITask.Api.Common.Ordering;

namespace ITask.Api.Domain;

/// <summary>A task in a list. <see cref="Number"/> is unique per board and forms the key (e.g. ITK-42).</summary>
public class Card
{
    public const int TitleMaxLength = 200;

    public const int DescriptionMaxLength = 10_000;

    public const int EmojiMaxLength = 16;

    /// <summary>Most cards one board can hold (archived ones included).</summary>
    public const int MaxPerBoard = 2_000;

    /// <summary>Most labels or assignees one card can carry.</summary>
    public const int MaxLabelsOrAssignees = 10;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BoardId { get; set; }

    public Board? Board { get; set; }

    public Guid ListId { get; set; }

    public BoardList? List { get; set; }

    public int Number { get; set; }

    public required string Title { get; set; }

    /// <summary>One emoji shown before the title.</summary>
    public string? Emoji { get; set; }

    /// <summary>Lower-cased <see cref="Title"/> for search; set by <c>AppDbContext</c> on save.</summary>
    public string SearchTitle { get; set; } = "";

    /// <summary>Markdown.</summary>
    public string? Description { get; set; }

    /// <summary>Sort key within the list; see <see cref="PositionCalculator"/>.</summary>
    public double Position { get; set; }

    public DateTimeOffset? DueAt { get; set; }

    public DateTimeOffset? StartAt { get; set; }

    public CardPriority Priority { get; set; }

    /// <summary>Hex colour (#RRGGBB) for the card's cover strip.</summary>
    public string? CoverColor { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Optimistic concurrency token. SQL Server generates it; on SQLite the context stamps it.</summary>
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public Guid CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<CardLabel> Labels { get; } = [];

    public List<CardAssignee> Assignees { get; } = [];

    public List<ChecklistItem> Checklist { get; } = [];

    public List<Comment> Comments { get; } = [];

    public List<Attachment> Attachments { get; } = [];

    /// <summary>Display key such as ITK-42.</summary>
    public static string Key(string prefix, int number) => $"{prefix}-{number}";
}
