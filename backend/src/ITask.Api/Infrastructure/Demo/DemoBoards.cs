using System.Text.Json;
using System.Text.Json.Serialization;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Automations;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Notifications;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;

namespace ITask.Api.Infrastructure.Demo;

/// <summary>
/// The demo workspace from <c>demo-boards.json</c>: boards, lists, labels and cards with their
/// checklists, comments and repeats, plus the activity and notifications that history would have
/// produced. Dates are relative to now, so due-soon lists and the calendar always have content.
/// </summary>
public static class DemoBoards
{
    private const string Resource = "ITask.Api.Infrastructure.Demo.demo-boards.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The parsed data file, read once.</summary>
    public static DemoData Data { get; } = Load();

    /// <summary>The non-signing-in teammates that fill the boards (assignees, comment authors).</summary>
    public static IReadOnlyList<DemoPerson> People => Data.People;

    /// <summary>
    /// Adds every board to <paramref name="db"/>. <paramref name="people"/> maps the data's person keys
    /// (demo account keys and teammate keys) to their users.
    /// </summary>
    public static void Add(AppDbContext db, IReadOnlyDictionary<string, AppUser> people, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(people);

        foreach (var spec in Data.Boards)
        {
            new Builder(db, people, now, spec).Build();
        }
    }

    private static DemoData Load()
    {
        using var stream = typeof(DemoBoards).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"{Resource} is not embedded.");
        return JsonSerializer.Deserialize<DemoData>(stream, Json) ?? throw new JsonException($"{Resource} is empty.");
    }

    private sealed class Builder(AppDbContext db, IReadOnlyDictionary<string, AppUser> people, DateTimeOffset now, DemoBoardSpec spec)
    {
        private readonly DateOnly _today = DateOnly.FromDateTime(now.UtcDateTime);
        private Board _board = null!;

        public void Build()
        {
            var created = now.AddDays(-spec.CreatedDaysAgo);
            var owner = spec.Members.First(m => m.Value == BoardRole.Owner).Key;

            _board = new Board
            {
                Name = spec.Name,
                Emoji = spec.Emoji,
                Color = spec.Color,
                KeyPrefix = spec.KeyPrefix,
                Description = spec.Description,
                CreatedAt = created,
                ArchivedAt = spec.ArchivedDaysAgo is { } archived ? now.AddDays(-archived) : null,
            };

            foreach (var (key, role) in spec.Members)
            {
                _board.Members.Add(new BoardMember { UserId = people[key].Id, Role = role, JoinedAt = created });
            }

            foreach (var key in spec.StarredBy)
            {
                _board.Stars.Add(new BoardStar { UserId = people[key].Id, CreatedAt = created.AddDays(1) });
            }

            var position = 0d;
            foreach (var list in spec.Lists)
            {
                position += PositionCalculator.Step;
                _board.Lists.Add(new BoardList
                {
                    Name = list.Name,
                    Emoji = list.Emoji,
                    WipLimit = list.WipLimit,
                    Position = position,
                    CreatedAt = created,
                });
            }

            var labels = spec.Labels.ToDictionary(
                l => l.Name,
                l => new Label { Name = l.Name, Color = l.Color, Emoji = l.Emoji });
            _board.Labels.AddRange(labels.Values);
            db.Boards.Add(_board);

            Activity(owner, ActivityTypes.BoardCreated, null, created, new { name = _board.Name });
            foreach (var (key, role) in spec.Members.Where(m => m.Key != owner))
            {
                var user = people[key];
                Activity(key, ActivityTypes.MemberJoined, null, created.AddHours(1), new
                {
                    member = new { id = user.Id, name = user.DisplayName },
                    role = role.ToString(),
                });
            }

            var positions = new Dictionary<int, double>();
            foreach (var cardSpec in spec.Cards)
            {
                var cardPosition = positions.GetValueOrDefault(cardSpec.List) + PositionCalculator.Step;
                positions[cardSpec.List] = cardPosition;
                AddCard(cardSpec, cardPosition, labels);
            }

            if (ReferenceEquals(spec, Data.Boards[0]))
            {
                AddAutomations(people[owner].Id, created, labels);
            }
        }

        /// <summary>A doorway and two rules on the first board, so the demo shows automations at work.</summary>
        private void AddAutomations(Guid ownerId, DateTimeOffset created, Dictionary<string, Label> labels)
        {
            var review = _board.Lists.FirstOrDefault(l => l.Name == "Review");
            var done = _board.Lists.FirstOrDefault(l => l.Name == "Done");
            if (review is null || done is null)
            {
                return;
            }

            void Add(AutomationKind kind, string name, AutomationSpec automation, Guid? listId = null, DoorwayMode? mode = null)
            {
                var rule = new AutomationRule
                {
                    BoardId = _board.Id,
                    Kind = kind,
                    Name = name,
                    ListId = listId,
                    Direction = listId is null ? null : DoorwayDirection.Enter,
                    Mode = mode,
                    CreatedById = ownerId,
                    CreatedAt = created.AddHours(2),
                };
                automation.WriteTo(rule);
                db.AutomationRules.Add(rule);
            }

            Add(AutomationKind.Doorway, "Review gate",
                new AutomationSpec(AutomationSpec.DoorwayTrigger(review.Id, DoorwayDirection.Enter), [new ConditionSpec(AutomationCatalog.IfAssigneeSet)], []),
                review.Id, DoorwayMode.Block);
            Add(AutomationKind.Rule, "Finish flow",
                new AutomationSpec(new TriggerSpec(AutomationCatalog.CardCompleted), [], [new ActionSpec(AutomationCatalog.DoMove, ListId: done.Id)]));
            if (labels.TryGetValue("Bug", out var bug))
            {
                Add(AutomationKind.Rule, "Bug priority",
                    new AutomationSpec(new TriggerSpec(AutomationCatalog.LabelAdded, LabelId: bug.Id), [],
                        [new ActionSpec(AutomationCatalog.DoPriority, Priority: CardPriority.High)]));
            }
        }

        private void AddCard(DemoCardSpec cardSpec, double position, Dictionary<string, Label> labels)
        {
            var list = _board.Lists[cardSpec.List];
            var done = spec.Lists[cardSpec.List].Done;
            var created = Clamp(now.AddDays(-cardSpec.CreatedDaysAgo).AddHours(-(_board.NextCardNumber % 7)));
            var creator = people[cardSpec.CreatedBy];
            var worker = cardSpec.Assignees.Count > 0 ? cardSpec.Assignees[0] : cardSpec.CreatedBy;

            var card = new Card
            {
                Board = _board,
                List = list,
                Number = _board.NextCardNumber++,
                Title = cardSpec.Title,
                Emoji = cardSpec.Emoji,
                Description = cardSpec.Description,
                Priority = cardSpec.Priority,
                CoverColor = cardSpec.Cover,
                Position = position,
                DueAt = cardSpec.Due is { } due ? At(due, 17) : null,
                StartAt = cardSpec.Start is { } start ? At(start, 9) : null,
                CompletedAt = done ? now.AddDays(-(cardSpec.CompletedDaysAgo ?? 1)) : null,
                CreatedById = creator.Id,
                CreatedAt = created,
            };

            foreach (var name in cardSpec.Labels)
            {
                card.Labels.Add(new CardLabel { Label = labels[name] });
            }

            foreach (var key in cardSpec.Assignees)
            {
                card.Assignees.Add(new CardAssignee { UserId = people[key].Id });
            }

            var itemPosition = 0d;
            foreach (var entry in cardSpec.Checklist)
            {
                itemPosition += PositionCalculator.Step;
                card.Checklist.Add(new ChecklistItem
                {
                    Text = entry[0].GetString()!,
                    IsDone = entry[1].GetBoolean() || done,
                    Position = itemPosition,
                    CreatedAt = created,
                });
            }

            db.Cards.Add(card);

            var firstList = _board.Lists[0];
            Activity(cardSpec.CreatedBy, ActivityTypes.CardCreated, card, created, new { list = (done ? firstList : list).Name });
            if (cardSpec.Assignees.Count > 0)
            {
                Activity(cardSpec.CreatedBy, ActivityTypes.CardAssigneesChanged, card, created.AddMinutes(5), new
                {
                    added = cardSpec.Assignees.Select(k => people[k].DisplayName).ToList(),
                });
                foreach (var key in cardSpec.Assignees.Where(k => k != cardSpec.CreatedBy))
                {
                    Notify(key, NotificationTypes.Assigned, card, cardSpec.CreatedBy, created.AddMinutes(5));
                }
            }

            foreach (var item in card.Checklist.Where(i => i.IsDone))
            {
                Activity(worker, ActivityTypes.ChecklistChecked, card, Clamp(created.AddHours(20)), new { item = item.Text });
            }

            if (done)
            {
                Activity(worker, ActivityTypes.CardCompleted, card, card.CompletedAt!.Value, null);
            }
            else if (list != firstList)
            {
                var moved = Clamp(created.AddHours(Math.Max(1, (now - created).TotalHours / 2)));
                Activity(worker, ActivityTypes.CardMoved, card, moved, new { fromList = firstList.Name, toList = list.Name });
            }

            if (cardSpec.Repeat is { } repeat)
            {
                AddRepeat(card, repeat, cardSpec.CreatedBy, created);
            }

            foreach (var comment in cardSpec.Comments)
            {
                AddComment(card, cardSpec.CreatedBy, comment);
            }
        }

        private void AddRepeat(Card card, DemoRepeatSpec repeat, string by, DateTimeOffset created)
        {
            var recurrence = new CardRecurrence
            {
                Card = card,
                Kind = repeat.Kind,
                Weekdays = Recurrences.Mask(repeat.Weekdays),
                DayOfMonth = repeat.DayOfMonth,
                TargetListId = _board.Lists[0].Id,
                TimeZone = "UTC",
                CreatedById = people[by].Id,
                CreatedAt = created,
            };
            recurrence.NextRunAt = Recurrences.FirstRun(RecurrenceRule.Of(recurrence), now, TimeZoneInfo.Utc);
            db.CardRecurrences.Add(recurrence);

            Activity(by, ActivityTypes.CardRecurrenceSet, card, created.AddMinutes(10), new
            {
                kind = repeat.Kind.ToString(),
                weekdays = repeat.Weekdays,
                dayOfMonth = repeat.DayOfMonth,
            });
        }

        private void AddComment(Card card, string cardCreator, DemoCommentSpec commentSpec)
        {
            var at = Clamp(card.CreatedAt.AddHours(1) > now.AddHours(-commentSpec.HoursAgo)
                ? card.CreatedAt.AddHours(1)
                : now.AddHours(-commentSpec.HoursAgo));
            var comment = new Comment { Card = card, AuthorId = people[commentSpec.By].Id, Body = commentSpec.Body, CreatedAt = at };

            var mentioned = spec.Members.Keys
                .Where(k => k != commentSpec.By && commentSpec.Body.Contains('@' + people[k].DisplayName, StringComparison.Ordinal))
                .ToList();
            foreach (var key in mentioned)
            {
                comment.Mentions.Add(new CommentMention { UserId = people[key].Id });
                Notify(key, NotificationTypes.Mentioned, card, commentSpec.By, at);
            }

            if (cardCreator != commentSpec.By && !mentioned.Contains(cardCreator))
            {
                Notify(cardCreator, NotificationTypes.CommentOnMyCard, card, commentSpec.By, at);
            }

            db.Comments.Add(comment);
            Activity(commentSpec.By, ActivityTypes.CommentAdded, card, at, new { commentId = comment.Id });
        }

        private void Activity(string actor, string type, Card? card, DateTimeOffset at, object? data)
        {
            db.Activity.Add(new ActivityEntry
            {
                Board = _board,
                CardId = card?.Id,
                ActorId = people[actor].Id,
                Type = type,
                Data = card is null ? Serialize(data) : NotificationData.WithCard(data, Card.Key(_board.KeyPrefix, card.Number), card.Title),
                CreatedAt = at,
            });
        }

        /// <summary>Notifies a demo account (teammates never sign in, archived boards stay quiet).</summary>
        private void Notify(string recipient, string type, Card card, string actor, DateTimeOffset at)
        {
            if (DemoAccounts.Find(recipient) is null || _board.ArchivedAt is not null)
            {
                return;
            }

            db.Notifications.Add(new Notification
            {
                UserId = people[recipient].Id,
                Type = type,
                Board = _board,
                CardId = card.Id,
                ActorId = people[actor].Id,
                Data = NotificationData.WithCard(null, Card.Key(_board.KeyPrefix, card.Number), card.Title),
                CreatedAt = at,
                ReadAt = now - at > TimeSpan.FromDays(2) ? at.AddHours(2) : null,
            });
        }

        private DateTimeOffset At(int days, int hour) =>
            new(_today.AddDays(days).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);

        private DateTimeOffset Clamp(DateTimeOffset at) => at < now ? at : now.AddMinutes(-5);

        private static string Serialize(object? data) => data is null ? "{}" : JsonSerializer.Serialize(data, Json);
    }
}

public sealed record DemoData(IReadOnlyList<DemoPerson> People, IReadOnlyList<DemoBoardSpec> Boards);

/// <summary>A teammate who appears on the demo boards but has no sign-in.</summary>
public sealed record DemoPerson(string Key, string Name, string Color);

public sealed record DemoBoardSpec(
    string Name,
    string Emoji,
    string Color,
    string KeyPrefix,
    string Description,
    int CreatedDaysAgo,
    int? ArchivedDaysAgo,
    IReadOnlyDictionary<string, BoardRole> Members,
    IReadOnlyList<string> StarredBy,
    IReadOnlyList<DemoListSpec> Lists,
    IReadOnlyList<DemoLabelSpec> Labels,
    IReadOnlyList<DemoCardSpec> Cards);

/// <summary>A list; cards in a <c>Done</c> list are completed.</summary>
public sealed record DemoListSpec(string Name, string? Emoji, int? WipLimit, bool Done);

public sealed record DemoLabelSpec(string Name, string Color, string? Emoji);

/// <summary>
/// A card. <c>Due</c> and <c>Start</c> are days from today; <c>Checklist</c> entries are <c>[text, done]</c>.
/// </summary>
public sealed record DemoCardSpec(
    int List,
    string Title,
    string? Emoji,
    CardPriority Priority,
    string CreatedBy,
    int CreatedDaysAgo,
    int? CompletedDaysAgo,
    int? Due,
    int? Start,
    string? Cover,
    string? Description,
    DemoRepeatSpec? Repeat)
{
    public IReadOnlyList<string> Labels { get; init; } = [];

    public IReadOnlyList<string> Assignees { get; init; } = [];

    public IReadOnlyList<JsonElement[]> Checklist { get; init; } = [];

    public IReadOnlyList<DemoCommentSpec> Comments { get; init; } = [];
}

public sealed record DemoRepeatSpec(RecurrenceKind Kind, int? DayOfMonth)
{
    /// <summary>0 (Sunday) to 6.</summary>
    public IReadOnlyList<int> Weekdays { get; init; } = [];
}

public sealed record DemoCommentSpec(string By, int HoursAgo, string Body);
