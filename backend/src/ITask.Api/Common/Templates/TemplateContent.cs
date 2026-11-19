using System.Text.Json;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Templates;

public sealed record TemplateList(string Name, string? Emoji = null, int? WipLimit = null);

public sealed record TemplateLabel(string Name, string Color, string? Emoji = null);

/// <summary>A card skeleton: no dates, assignees, comments or files. <c>List</c> and <c>Labels</c> refer by name.</summary>
public sealed record TemplateCard(
    string List,
    string Title,
    string? Emoji = null,
    string? Description = null,
    CardPriority Priority = CardPriority.None,
    IReadOnlyList<string>? Labels = null,
    IReadOnlyList<string>? Checklist = null);

/// <summary>What a board template creates: lists in order, labels and card skeletons.</summary>
public sealed record TemplateContent(
    IReadOnlyList<TemplateList> Lists,
    IReadOnlyList<TemplateLabel>? Labels = null,
    IReadOnlyList<TemplateCard>? Cards = null)
{
    public const int MaxCards = 200;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TemplateContent Parse(string json) =>
        JsonSerializer.Deserialize<TemplateContent>(json, Json) ?? throw new JsonException("Empty template content.");

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    /// <summary>
    /// Adds the lists, labels and cards to a new <paramref name="board"/>, numbering cards from the board's
    /// next number. Cards naming an unknown list are skipped; unknown label names are ignored.
    /// </summary>
    public void ApplyTo(Board board, Guid userId)
    {
        ArgumentNullException.ThrowIfNull(board);

        var lists = Lists
            .Take(BoardList.MaxActivePerBoard)
            .DistinctBy(l => l.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select((l, i) => new BoardList
            {
                Name = l.Name.Trim(),
                Emoji = l.Emoji,
                WipLimit = l.WipLimit,
                Position = PositionCalculator.Step * (i + 1),
            })
            .ToList();
        board.Lists.AddRange(lists);

        var labels = (Labels ?? [])
            .Take(Label.MaxPerBoard)
            .DistinctBy(l => l.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(l => new Label { Name = l.Name.Trim(), Color = l.Color.ToUpperInvariant(), Emoji = l.Emoji })
            .ToList();
        board.Labels.AddRange(labels);

        var listsByName = lists.ToDictionary(l => l.Name, StringComparer.OrdinalIgnoreCase);
        var labelsByName = labels.ToDictionary(l => l.Name, StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<BoardList, int>();
        foreach (var skeleton in (Cards ?? []).Take(MaxCards))
        {
            if (!listsByName.TryGetValue(skeleton.List.Trim(), out var list))
            {
                continue;
            }

            counts[list] = counts.GetValueOrDefault(list) + 1;
            var card = new Card
            {
                List = list,
                Number = board.NextCardNumber++,
                Title = skeleton.Title.Trim(),
                Emoji = skeleton.Emoji,
                Description = skeleton.Description,
                Priority = skeleton.Priority,
                Position = PositionCalculator.Step * counts[list],
                CreatedById = userId,
            };
            card.Labels.AddRange((skeleton.Labels ?? [])
                .Select(name => labelsByName.GetValueOrDefault(name.Trim()))
                .OfType<Label>()
                .Distinct()
                .Select(label => new CardLabel { Label = label }));
            card.Checklist.AddRange((skeleton.Checklist ?? [])
                .Take(ChecklistItem.MaxPerCard)
                .Select((text, i) => new ChecklistItem { Text = text.Trim(), Position = PositionCalculator.Step * (i + 1) }));
            board.Cards.Add(card);
        }
    }

    /// <summary>The board's active lists, its labels and, when asked, its active cards as skeletons.</summary>
    public static async Task<TemplateContent> FromBoardAsync(AppDbContext db, Guid boardId, bool includeCards, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        var lists = await db.Lists
            .AsNoTracking()
            .Where(l => l.BoardId == boardId && l.ArchivedAt == null)
            .OrderBy(l => l.Position)
            .Select(l => new TemplateList(l.Name, l.Emoji, l.WipLimit))
            .ToListAsync(ct);
        var labels = await db.Labels
            .AsNoTracking()
            .Where(l => l.BoardId == boardId)
            .OrderBy(l => l.Name)
            .Select(l => new TemplateLabel(l.Name, l.Color, l.Emoji))
            .ToListAsync(ct);

        List<TemplateCard> cards = [];
        if (includeCards)
        {
            var rows = await db.Cards
                .AsNoTracking()
                .Where(c => c.BoardId == boardId && c.ArchivedAt == null && c.List!.ArchivedAt == null)
                .OrderBy(c => c.List!.Position)
                .ThenBy(c => c.Position)
                .Take(MaxCards)
                .Select(c => new
                {
                    List = c.List!.Name,
                    c.Title,
                    c.Emoji,
                    c.Description,
                    c.Priority,
                    Labels = c.Labels.Select(l => l.Label!.Name).ToList(),
                    Checklist = c.Checklist.OrderBy(i => i.Position).Select(i => i.Text).ToList(),
                })
                .ToListAsync(ct);
            cards = [.. rows.Select(c => new TemplateCard(
                c.List, c.Title, c.Emoji, c.Description, c.Priority, c.Labels, c.Checklist))];
        }

        return new TemplateContent(lists, labels, cards);
    }
}

/// <summary>A template as the gallery shows it. <c>Source</c> is <c>builtIn</c> (slug id) or <c>saved</c> (GUID id).</summary>
public sealed record TemplateSummary(
    string Id,
    string Source,
    string Name,
    string? Emoji,
    string? Description,
    string Color,
    IReadOnlyList<string> Lists,
    int LabelCount,
    int CardCount)
{
    public const string BuiltIn = "builtIn";

    public const string Saved = "saved";

    public static TemplateSummary Of(string id, string source, string name, string? emoji, string? description, string color, TemplateContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new TemplateSummary(
            id,
            source,
            name,
            emoji,
            description,
            color,
            [.. content.Lists.Select(l => l.Name)],
            content.Labels?.Count ?? 0,
            content.Cards?.Count ?? 0);
    }

    public static TemplateSummary Of(BoardTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return Of(template.Id.ToString(), Saved, template.Name, template.Emoji, template.Description, template.Color, TemplateContent.Parse(template.Content));
    }
}
