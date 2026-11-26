using System.Text.Json;
using System.Text.Json.Nodes;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Notifications;
using ITask.Api.Common.Ordering;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Automations;

/// <summary>
/// Runs board automations inside the save that triggered them. Triggers are read from the activity
/// entries the save adds and from the tracked card's pending changes; actions change only that card
/// and are recorded as activity carrying <c>automation: { ruleId, name, runId }</c>. Entries with that
/// marker never trigger automations, so a run can't set off another one. Nothing here saves.
/// </summary>
public sealed partial class AutomationEngine(TimeProvider clock, AutomationRunLog log, ILogger<AutomationEngine> logger)
{
    /// <summary>Most automation runs one save can cause.</summary>
    public const int MaxRunsPerSave = 10;

    private static readonly string[] PriorityField = ["priority"];
    private static readonly string[] DueField = ["dueAt"];

    private readonly record struct CardEvent(string Type, Guid? ListId = null, Guid? LabelId = null, bool Mentions = false);

    /// <summary>Applies the automations triggered by the pending changes in <paramref name="db"/>.</summary>
    public async Task RunAsync(AppDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.SuppressAutomations)
        {
            return;
        }

        db.ChangeTracker.DetectChanges();
        var entries = db.ChangeTracker.Entries<ActivityEntry>()
            .Where(e => e.State == EntityState.Added && e.Entity.CardId is not null && !AutomationMarker.Has(e.Entity.Data))
            .Select(e => e.Entity)
            .ToList();
        if (entries.Count == 0)
        {
            return;
        }

        var boardIds = entries.Select(e => e.BoardId).Distinct().ToList();
        var rules = (await db.AutomationRules.AsNoTracking()
                .Where(r => boardIds.Contains(r.BoardId) && r.Enabled)
                .ToListAsync(ct))
            .OrderBy(r => r.CreatedAt)
            .Select(r => (Rule: r, Spec: AutomationSpec.Of(r)))
            .Where(r => r.Rule.Kind == AutomationKind.Doorway || !AutomationCatalog.IsHourly(r.Spec.Trigger.Type))
            .ToList();
        if (rules.Count == 0)
        {
            return;
        }

        var runs = 0;
        var boards = new Dictionary<Guid, BoardContext>();
        var disabled = new HashSet<Guid>();
        foreach (var group in entries.GroupBy(e => e.CardId!.Value))
        {
            var card = db.Cards.Local.FirstOrDefault(c => c.Id == group.Key)
                ?? await db.Cards.FirstOrDefaultAsync(c => c.Id == group.Key, ct);
            if (card is null || card.ArchivedAt is not null)
            {
                continue;
            }

            var boardRules = rules.Where(r => r.Rule.BoardId == card.BoardId).ToList();
            if (boardRules.Count == 0)
            {
                continue;
            }

            var actorId = group.First().ActorId;
            var state = await CardState.LoadAsync(db, card, ct);
            var events = Events(db, card, group, state);

            // Doorways first: they describe the list a card is in, rules react to it.
            foreach (var (rule, spec) in boardRules.OrderBy(r => r.Rule.Kind == AutomationKind.Doorway ? 0 : 1))
            {
                var trigger = rule.Kind == AutomationKind.Doorway && rule.ListId is { } listId && rule.Direction is { } direction
                    ? AutomationSpec.DoorwayTrigger(listId, direction)
                    : spec.Trigger;
                if (disabled.Contains(rule.Id) || !events.Any(e => Matches(trigger, e)))
                {
                    continue;
                }

                if (runs >= MaxRunsPerSave)
                {
                    LogCapped(logger, card.Id);
                    return;
                }

                if (!boards.TryGetValue(card.BoardId, out var board))
                {
                    boards[card.BoardId] = board = await AutomationBoards.LoadAsync(db, card.BoardId, ct);
                }

                if (AutomationCatalog.Validate(rule.Kind, rule.ListId, rule.Direction, rule.Mode, spec, board).Count > 0)
                {
                    Disable(db, rule, actorId, card.Id, "It refers to a list, label or member that is gone.");
                    disabled.Add(rule.Id);
                    continue;
                }

                List<ActionSpec> actions;
                if (rule.Kind == AutomationKind.Rule)
                {
                    if (!spec.Conditions.All(state.Meets))
                    {
                        continue;
                    }

                    actions = [.. spec.Actions];
                }
                else
                {
                    var fixes = rule.Mode == DoorwayMode.Fix
                        ? spec.Conditions.Where(c => !state.Meets(c)).SelectMany(c => Fixes(c, actorId))
                        : [];
                    actions = [.. fixes, .. spec.Actions];
                }

                if (await ApplyAsync(db, rule, actions, state, board, actorId, null, ct) is not null)
                {
                    runs++;
                }
            }
        }

        db.ChangeTracker.DetectChanges();
    }

    /// <summary>
    /// Runs <paramref name="actions"/> of <paramref name="rule"/> on the card in <paramref name="state"/> as
    /// <paramref name="actorId"/>. Returns the run, or null when every action was a no-op (nothing is recorded).
    /// </summary>
    public async Task<AutomationRun?> ApplyAsync(
        AppDbContext db, AutomationRule rule, IReadOnlyList<ActionSpec> actions, CardState state, BoardContext board,
        Guid actorId, string? key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(board);

        var now = clock.GetUtcNow();
        var card = state.Card;
        var run = new AutomationRun
        {
            RuleId = rule.Id,
            BoardId = card.BoardId,
            CardId = card.Id,
            TriggeredById = actorId,
            Key = key,
            CreatedAt = now,
        };
        var marker = new JsonObject { ["ruleId"] = rule.Id, ["name"] = rule.Name, ["runId"] = run.Id };
        var prefix = db.Boards.Local.FirstOrDefault(b => b.Id == card.BoardId)?.KeyPrefix
            ?? await db.Boards.AsNoTracking().Where(b => b.Id == card.BoardId).Select(b => b.KeyPrefix).FirstAsync(ct);
        var changes = new RunChanges();
        var summary = new List<string>();
        var aiQueued = false;

        void Record(string type, object? data)
        {
            var node = data is null ? [] : JsonSerializer.SerializeToNode(data, AutomationSpec.Json)!.AsObject();
            node["card"] = new JsonObject { ["key"] = Card.Key(prefix, card.Number), ["title"] = card.Title };
            node[AutomationMarker.Property] = marker.DeepClone();
            db.Activity.Add(new ActivityEntry
            {
                Type = type,
                BoardId = card.BoardId,
                CardId = card.Id,
                ActorId = actorId,
                Data = node.ToJsonString(AutomationSpec.Json),
                CreatedAt = now,
            });
        }

        string Member(Guid id) => board.Members.TryGetValue(id, out var m) ? m.Name : "a member";

        foreach (var action in actions)
        {
            switch (action.Type)
            {
                case AutomationCatalog.DoMove when action.ListId is { } target && target != card.ListId && board.Lists.ContainsKey(target):
                {
                    var from = card.ListId;
                    var position = await PositionAsync(db, card.Id, target, action.Position == "top", ct);
                    changes.List ??= new ListChange(from, card.Position, target, position);
                    changes.List = changes.List with { ToId = target, ToPosition = position };
                    card.ListId = target;
                    card.Position = position;
                    Record(ActivityTypes.CardMoved, new { fromList = board.Lists.GetValueOrDefault(from, "a list"), toList = board.Lists[target], fromListId = from, toListId = target });
                    summary.Add($"moved to {board.Lists[target]}");
                    break;
                }

                case AutomationCatalog.DoComplete when card.CompletedAt is null:
                    changes.CompletedAt = new DateChange(changes.CompletedAt?.Before ?? card.CompletedAt, now);
                    card.CompletedAt = now;
                    Record(ActivityTypes.CardCompleted, null);
                    summary.Add("marked complete");
                    break;

                case AutomationCatalog.DoReopen when card.CompletedAt is not null:
                    changes.CompletedAt = new DateChange(changes.CompletedAt?.Before ?? card.CompletedAt, null);
                    card.CompletedAt = null;
                    Record(ActivityTypes.CardReopened, null);
                    summary.Add("reopened");
                    break;

                case AutomationCatalog.DoAddLabel when action.LabelId is { } labelId
                    && board.Labels.TryGetValue(labelId, out var addName) && !state.Labels.Contains(labelId)
                    && state.Labels.Count < Card.MaxLabelsOrAssignees:
                    SetLink(db, new CardLabel { CardId = card.Id, LabelId = labelId }, add: true);
                    state.Labels.Add(labelId);
                    changes.LabelsAdded.Add(labelId);
                    Record(ActivityTypes.CardLabelsChanged, new { added = new[] { addName }, removed = Array.Empty<string>() });
                    summary.Add($"added {addName}");
                    break;

                case AutomationCatalog.DoRemoveLabel when action.LabelId is { } labelId && state.Labels.Contains(labelId):
                {
                    var name = board.Labels.GetValueOrDefault(labelId, "a label");
                    SetLink(db, new CardLabel { CardId = card.Id, LabelId = labelId }, add: false);
                    state.Labels.Remove(labelId);
                    changes.LabelsRemoved.Add(labelId);
                    Record(ActivityTypes.CardLabelsChanged, new { added = Array.Empty<string>(), removed = new[] { name } });
                    summary.Add($"removed {name}");
                    break;
                }

                case AutomationCatalog.DoAssign:
                {
                    Guid? userId = action.Who switch
                    {
                        "actor" => actorId,
                        "creator" => card.CreatedById,
                        _ => action.UserId,
                    };
                    if (userId is not { } id || !board.Members.ContainsKey(id) || state.Assignees.Contains(id)
                        || state.Assignees.Count >= Card.MaxLabelsOrAssignees)
                    {
                        break;
                    }

                    SetLink(db, new CardAssignee { CardId = card.Id, UserId = id }, add: true);
                    state.Assignees.Add(id);
                    changes.AssigneesAdded.Add(id);
                    Record(ActivityTypes.CardAssigneesChanged, new { added = new[] { Member(id) }, removed = Array.Empty<string>() });
                    Notify(db, NotificationTypes.Assigned, [id], card, prefix, actorId, null, now);
                    summary.Add($"assigned {Member(id)}");
                    break;
                }

                case AutomationCatalog.DoUnassign when state.Assignees.Count > 0:
                {
                    var removed = state.Assignees.ToList();
                    foreach (var id in removed)
                    {
                        SetLink(db, new CardAssignee { CardId = card.Id, UserId = id }, add: false);
                    }

                    state.Assignees.Clear();
                    changes.AssigneesRemoved.AddRange(removed);
                    Record(ActivityTypes.CardAssigneesChanged, new { added = Array.Empty<string>(), removed = removed.Select(Member).ToArray() });
                    summary.Add("unassigned everyone");
                    break;
                }

                case AutomationCatalog.DoPriority when action.Priority is { } priority && card.Priority != priority:
                    changes.Priority = new PriorityChange(changes.Priority?.Before ?? card.Priority, priority);
                    card.Priority = priority;
                    Record(ActivityTypes.CardUpdated, new { fields = PriorityField, priority = priority.ToString() });
                    summary.Add($"set priority {priority}");
                    break;

                case AutomationCatalog.DoDue when action.Days is { } days:
                {
                    var zoneId = await db.Users.AsNoTracking().Where(u => u.Id == actorId).Select(u => u.TimeZone).FirstOrDefaultAsync(ct);
                    var zone = Recurrences.Zone(zoneId ?? "UTC");
                    var due = Recurrences.At(Recurrences.LocalDate(now, zone).AddDays(days), new TimeOnly(23, 59, 59), zone);
                    if (card.DueAt == due)
                    {
                        break;
                    }

                    changes.DueAt = new DateChange(changes.DueAt is { } d ? d.Before : card.DueAt, due);
                    card.DueAt = due;
                    Record(ActivityTypes.CardUpdated, new { fields = DueField, dueAt = due });
                    summary.Add(days == 0 ? "set due today" : $"set due in {days} day{(days == 1 ? "" : "s")}");
                    break;
                }

                case AutomationCatalog.DoChecklist when action.Items is { Count: > 0 } items:
                {
                    var room = ChecklistItem.MaxPerCard - state.ChecklistTotal - changes.ChecklistItems.Count;
                    var last = await db.ChecklistItems.Where(i => i.CardId == card.Id).MaxAsync(i => (double?)i.Position, ct);
                    last = db.ChangeTracker.Entries<ChecklistItem>()
                        .Where(e => e.Entity.CardId == card.Id && e.State == EntityState.Added)
                        .Select(e => (double?)e.Entity.Position)
                        .Append(last)
                        .Max();
                    foreach (var text in items.Take(Math.Max(0, room)))
                    {
                        last = PositionCalculator.Append(last);
                        var item = new ChecklistItem { CardId = card.Id, Text = text.Trim(), Position = last.Value, CreatedAt = now };
                        db.ChecklistItems.Add(item);
                        changes.ChecklistItems.Add(item.Id);
                        Record(ActivityTypes.ChecklistAdded, new { item = item.Text });
                    }

                    if (room > 0)
                    {
                        summary.Add("added checklist items");
                    }

                    break;
                }

                case AutomationCatalog.DoComment when !string.IsNullOrWhiteSpace(action.Text):
                {
                    var comment = new Comment
                    {
                        CardId = card.Id,
                        AuthorId = actorId,
                        Body = $"⚡ **{rule.Name}**: {action.Text.Trim()}",
                        CreatedAt = now,
                    };
                    db.Comments.Add(comment);
                    changes.CommentId = comment.Id;
                    Record(ActivityTypes.CommentAdded, new { commentId = comment.Id });
                    summary.Add("commented");
                    break;
                }

                case AutomationCatalog.DoNotify:
                {
                    var recipients = action.Who switch
                    {
                        "assignees" => [.. state.Assignees],
                        "owners" => board.Members.Where(m => m.Value.Role == BoardRole.Owner).Select(m => m.Key).ToList(),
                        _ => board.Members.Keys.ToList(),
                    };
                    if (Notify(db, NotificationTypes.Automation, recipients, card, prefix, actorId, new { rule = rule.Name, text = action.Text }, now) > 0)
                    {
                        summary.Add(action.Who == "assignees" ? "notified the assignees" : action.Who == "owners" ? "notified the owners" : "notified the board");
                    }

                    break;
                }

                case AutomationCatalog.DoAiBreakdown or AutomationCatalog.DoAiTriage or AutomationCatalog.DoAiSummary when !log.HasPendingAi:
                    log.AddAi(new AiActionJob(rule.Id, card.BoardId, card.Id, actorId, action.Type));
                    aiQueued = true;
                    summary.Add(action.Type switch
                    {
                        AutomationCatalog.DoAiBreakdown => "asked ✨ AI for a checklist",
                        AutomationCatalog.DoAiTriage => "asked ✨ AI to triage it",
                        _ => "asked ✨ AI for a summary",
                    });
                    break;

                case AutomationCatalog.DoArchive when card.ArchivedAt is null:
                    changes.ArchivedAt = new DateChange(null, now);
                    card.ArchivedAt = now;
                    Record(ActivityTypes.CardArchived, new { list = board.Lists.GetValueOrDefault(card.ListId, "a list") });
                    summary.Add("archived");
                    break;
            }
        }

        if (summary.Count == 0)
        {
            return null;
        }

        run.UndoJson = changes.IsEmpty ? null : JsonSerializer.Serialize(changes, AutomationSpec.Json);
        log.Add(new AutomationRunNotice(run.Id, rule.Name, Sentence(summary), run.UndoJson is not null));

        // A run that only queued AI changes nothing yet; the AI action records its own run when it lands.
        if (!(aiQueued && changes.IsEmpty))
        {
            db.AutomationRuns.Add(run);
        }

        return run;
    }

    /// <summary>Actions that fill in a requirement a fix-mode doorway found missing.</summary>
    public static IEnumerable<ActionSpec> Fixes(ConditionSpec condition, Guid actorId)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return condition.Type switch
        {
            AutomationCatalog.IfAssigneeSet => [new ActionSpec(AutomationCatalog.DoAssign, Who: "member", UserId: actorId)],
            AutomationCatalog.IfAssigneeIs => [new ActionSpec(AutomationCatalog.DoAssign, Who: "member", UserId: condition.UserId)],
            AutomationCatalog.IfAssigneeNone => [new ActionSpec(AutomationCatalog.DoUnassign)],
            AutomationCatalog.IfLabelAny => [new ActionSpec(AutomationCatalog.DoAddLabel, LabelId: condition.LabelIds is { Count: > 0 } ids ? ids[0] : null)],
            AutomationCatalog.IfLabelNone => (condition.LabelIds ?? []).Select(id => new ActionSpec(AutomationCatalog.DoRemoveLabel, LabelId: id)),
            AutomationCatalog.IfPriorityAtLeast => [new ActionSpec(AutomationCatalog.DoPriority, Priority: condition.Priority)],
            _ => [],
        };
    }

    private static List<CardEvent> Events(AppDbContext db, Card card, IEnumerable<ActivityEntry> entries, CardState state)
    {
        var events = new List<CardEvent>();
        var entry = db.Entry(card);
        if (entry.State == EntityState.Added)
        {
            events.Add(new CardEvent(AutomationCatalog.CardCreated, card.ListId));
            events.Add(new CardEvent(AutomationCatalog.CardEntered, card.ListId));
        }
        else if (entry.Property(c => c.ListId) is { IsModified: true } list && list.OriginalValue != list.CurrentValue)
        {
            events.Add(new CardEvent(AutomationCatalog.CardLeft, list.OriginalValue));
            events.Add(new CardEvent(AutomationCatalog.CardEntered, list.CurrentValue));
        }

        foreach (var link in db.ChangeTracker.Entries<CardLabel>().Where(e => e.Entity.CardId == card.Id))
        {
            if (link.State == EntityState.Added)
            {
                events.Add(new CardEvent(AutomationCatalog.LabelAdded, LabelId: link.Entity.LabelId));
            }
            else if (link.State == EntityState.Deleted)
            {
                events.Add(new CardEvent(AutomationCatalog.LabelRemoved, LabelId: link.Entity.LabelId));
            }
        }

        foreach (var type in entries.Select(e => e.Type).Distinct())
        {
            switch (type)
            {
                case ActivityTypes.CardCompleted:
                    events.Add(new CardEvent(AutomationCatalog.CardCompleted));
                    break;
                case ActivityTypes.CardReopened:
                    events.Add(new CardEvent(AutomationCatalog.CardReopened));
                    break;
                case ActivityTypes.CardAssigneesChanged:
                    events.Add(new CardEvent(AutomationCatalog.AssigneesChanged));
                    break;
                case ActivityTypes.ChecklistChecked when state.ChecklistComplete:
                    events.Add(new CardEvent(AutomationCatalog.ChecklistDone));
                    break;
                case ActivityTypes.CommentAdded:
                    var mentions = db.ChangeTracker.Entries<CommentMention>().Any(m => m.State == EntityState.Added);
                    events.Add(new CardEvent(AutomationCatalog.CommentAdded, Mentions: mentions));
                    break;
            }
        }

        return events;
    }

    private static bool Matches(TriggerSpec trigger, CardEvent e) =>
        trigger.Type == e.Type
        && (trigger.ListId is null || trigger.ListId == e.ListId)
        && (trigger.LabelId is null || trigger.LabelId == e.LabelId)
        && (trigger.Mentions != true || e.Mentions);

    private void Disable(AppDbContext db, AutomationRule rule, Guid actorId, Guid cardId, string reason)
    {
        db.AutomationRules.Attach(rule);
        rule.Enabled = false;
        db.Activity.Add(new ActivityEntry
        {
            Type = ActivityTypes.AutomationDisabled,
            BoardId = rule.BoardId,
            CardId = null,
            ActorId = actorId,
            Data = JsonSerializer.Serialize(
                new Dictionary<string, object> { ["rule"] = rule.Name, ["reason"] = reason, [AutomationMarker.Property] = new { ruleId = rule.Id, name = rule.Name } },
                AutomationSpec.Json),
            CreatedAt = clock.GetUtcNow(),
        });
        LogDisabled(logger, rule.Id, cardId);
    }

    /// <summary>Adds or removes a link row, undoing a pending opposite change instead of clashing with it.</summary>
    private static void SetLink<T>(AppDbContext db, T link, bool add)
        where T : class
    {
        var existing = db.ChangeTracker.Entries<T>().FirstOrDefault(e => db.Model.FindEntityType(typeof(T))!
            .FindPrimaryKey()!.Properties.All(p => Equals(e.Property(p.Name).CurrentValue, db.Entry(link).Property(p.Name).CurrentValue)));
        if (existing is not null)
        {
            existing.State = (existing.State, add) switch
            {
                (EntityState.Deleted, true) => EntityState.Unchanged,
                (EntityState.Added, false) => EntityState.Detached,
                (_, false) => EntityState.Deleted,
                _ => existing.State,
            };
            return;
        }

        if (add)
        {
            db.Add(link);
        }
        else
        {
            db.Attach(link);
            db.Remove(link);
        }
    }

    private static async Task<double> PositionAsync(AppDbContext db, Guid cardId, Guid listId, bool top, CancellationToken ct)
    {
        var others = db.Cards.AsNoTracking().Where(c => c.ListId == listId && c.ArchivedAt == null && c.Id != cardId);
        if (top)
        {
            var first = await others.MinAsync(c => (double?)c.Position, ct);
            return first is null ? PositionCalculator.Step : PositionCalculator.Between(null, first);
        }

        return PositionCalculator.Append(await others.MaxAsync(c => (double?)c.Position, ct));
    }

    private static int Notify(
        AppDbContext db, string type, IEnumerable<Guid> userIds, Card card, string prefix, Guid actorId, object? data, DateTimeOffset now)
    {
        var json = NotificationData.WithCard(data, Card.Key(prefix, card.Number), card.Title);
        var count = 0;
        foreach (var userId in userIds.Where(id => id != actorId).Distinct())
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = type,
                BoardId = card.BoardId,
                CardId = card.Id,
                ActorId = actorId,
                Data = json,
                CreatedAt = now,
            });
            count++;
        }

        return count;
    }

    private static string Sentence(List<string> parts) =>
        string.Concat(char.ToUpperInvariant(parts[0][0]).ToString(), parts[0][1..], parts.Count > 1 ? ", " + string.Join(", ", parts.Skip(1)) : "");

    [LoggerMessage(Level = LogLevel.Warning, Message = "Automations for card {CardId} stopped after the per-save limit.")]
    private static partial void LogCapped(ILogger logger, Guid cardId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Automation {RuleId} disabled while running on card {CardId}: it refers to something that is gone.")]
    private static partial void LogDisabled(ILogger logger, Guid ruleId, Guid cardId);
}

/// <summary>The <c>automation</c> property on activity entries written by automations.</summary>
public static class AutomationMarker
{
    public const string Property = "automation";

    public static bool Has(string data) => data.Contains("\"automation\":", StringComparison.Ordinal);
}

/// <summary>Lists, labels and members of a board, for validating and describing automations.</summary>
public static class AutomationBoards
{
    public static async Task<BoardContext> LoadAsync(AppDbContext db, Guid boardId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var lists = await db.Lists.AsNoTracking()
            .Where(l => l.BoardId == boardId && l.ArchivedAt == null)
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var labels = await db.Labels.AsNoTracking()
            .Where(l => l.BoardId == boardId)
            .ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var members = await db.BoardMembers.AsNoTracking()
            .Where(m => m.BoardId == boardId)
            .Select(m => new { m.UserId, m.User!.DisplayName, m.Role })
            .ToListAsync(ct);
        return new BoardContext(lists, labels, members.ToDictionary(m => m.UserId, m => (m.DisplayName, m.Role)));
    }
}
