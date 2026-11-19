using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using ITask.Api.Common.Activity;
using ITask.Api.Common.Ai;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Automations;

/// <summary>A ✨ AI action an automation queued: run it on the card once the change that triggered it is saved.</summary>
public sealed record AiActionJob(Guid RuleId, Guid BoardId, Guid CardId, Guid TriggeredById, string Action);

/// <summary>
/// AI actions waiting to run, in memory. Bounded: when it is full new jobs are dropped (and logged),
/// so a burst can't pile up calls. Jobs lost on a restart are simply not run.
/// </summary>
public sealed class AutomationAiJobs
{
    public const int Capacity = 20;

    private readonly Channel<AiActionJob> _channel = Channel.CreateBounded<AiActionJob>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public bool TryAdd(AiActionJob job) => _channel.Writer.TryWrite(job);

    public ChannelReader<AiActionJob> Reader => _channel.Reader;
}

/// <summary>Runs queued AI actions one at a time, each in its own scope. Idle when nothing is queued.</summary>
internal sealed partial class AutomationAiWorker(AutomationAiJobs queue, IServiceScopeFactory scopes, ILogger<AutomationAiWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AutomationAiRunner>().RunAsync(job, stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(logger, e, job.RuleId, job.CardId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI action of automation {RuleId} on card {CardId} failed.")]
    private static partial void LogFailed(ILogger logger, Exception exception, Guid ruleId, Guid cardId);
}

/// <summary>
/// One AI action: asks the model with the existing prompts, charged to the rule creator's quota, and
/// applies the answer through <see cref="AutomationEngine.ApplyAsync"/> (so it is recorded, live and
/// undoable, and never triggers other automations). When it can't run, records <c>automation.ai_skipped</c>.
/// </summary>
public sealed class AutomationAiRunner(AppDbContext db, AiGateway gateway, IAiService ai, AutomationEngine engine, TimeProvider clock)
{
    private const int MaxSummaryLength = 1_000;

    public async Task RunAsync(AiActionJob job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var rule = await db.AutomationRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == job.RuleId && r.Enabled, ct);
        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == job.CardId && c.BoardId == job.BoardId && c.ArchivedAt == null, ct);
        if (rule is null || card is null)
        {
            return;
        }

        var board = await AutomationBoards.LoadAsync(db, job.BoardId, ct);
        var creator = await db.Users.AsNoTracking().Where(u => u.Id == rule.CreatedById).Select(u => new { u.IsDemo }).FirstOrDefaultAsync(ct);
        if (!ai.IsEnabled)
        {
            await SkipAsync(job, rule, card, "AI is off.", ct);
            return;
        }

        if (creator is null || creator.IsDemo
            || !board.Members.TryGetValue(rule.CreatedById, out var member) || member.Role < BoardRole.Editor)
        {
            await SkipAsync(job, rule, card, "The automation's creator can't use AI on this board.", ct);
            return;
        }

        gateway.ActAs(rule.CreatedById);
        var (actions, problem) = job.Action switch
        {
            AutomationCatalog.DoAiBreakdown => await BreakdownAsync(card, ct),
            AutomationCatalog.DoAiTriage => await TriageAsync(card, board, ct),
            AutomationCatalog.DoAiSummary => await SummaryAsync(card, rule.CreatedById, ct),
            _ => ([], "Unknown AI action."),
        };
        if (problem is not null)
        {
            await SkipAsync(job, rule, card, problem, ct);
            return;
        }

        var state = await CardState.LoadAsync(db, card, ct);
        await engine.ApplyAsync(db, rule, actions, state, board, job.TriggeredById, null, ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<(List<ActionSpec>, string?)> BreakdownAsync(Card card, CancellationToken ct)
    {
        var existing = await db.ChecklistItems.AsNoTracking().Where(i => i.CardId == card.Id)
            .OrderBy(i => i.Position).Select(i => i.Text).ToListAsync(ct);
        var prompt = new StringBuilder()
            .Append("Title: ").Append(card.Title).Append("\n\n")
            .Append("Description:\n").Append(AiText.Cut(card.Description, 6_000)).Append("\n\n")
            .Append("Existing checklist:\n").AppendJoin('\n', existing.Take(50).Select(i => "- " + AiText.Cut(i, 200)))
            .ToString();
        var outcome = await gateway.RunAsync(
            AiPrompts.Breakdown, prompt, 400, json => AiDrafts.ParseBreakdown(json, existing), ct, AiDrafts.BreakdownSchema);
        return outcome.Problem is { } p
            ? ([], Reason(p))
            : ([new ActionSpec(AutomationCatalog.DoChecklist, Items: [.. outcome.Value!.Take(AutomationCatalog.MaxChecklistItems)])], null);
    }

    private async Task<(List<ActionSpec>, string?)> TriageAsync(Card card, BoardContext board, CancellationToken ct)
    {
        var labels = board.Labels.Values.ToList();
        var lists = board.Lists.Values.ToList();
        var prompt = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Today: {DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime):yyyy-MM-dd}\n")
            .Append("Board labels: ").AppendJoin(", ", labels).Append('\n')
            .Append("Board lists: ").AppendJoin(", ", lists).Append("\n\n")
            .Append("Text:\n").Append(card.Title).Append("\n\n").Append(AiText.Cut(card.Description, 3_000))
            .ToString();
        var outcome = await gateway.RunAsync(
            AiPrompts.QuickAdd, prompt, 300, json => AiDrafts.ParseQuickAdd(json, labels, lists), ct, AiDrafts.QuickAddSchema);
        if (outcome.Problem is { } p)
        {
            return ([], Reason(p));
        }

        var actions = outcome.Value!.Labels
            .Select(name => board.Labels.First(l => string.Equals(l.Value, name, StringComparison.OrdinalIgnoreCase)).Key)
            .Select(id => new ActionSpec(AutomationCatalog.DoAddLabel, LabelId: id))
            .ToList();
        if (outcome.Value.Priority != CardPriority.None)
        {
            actions.Add(new ActionSpec(AutomationCatalog.DoPriority, Priority: outcome.Value.Priority));
        }

        return actions.Count > 0 ? (actions, null) : ([], "AI found no labels or priority to set.");
    }

    private async Task<(List<ActionSpec>, string?)> SummaryAsync(Card card, Guid creatorId, CancellationToken ct)
    {
        var zone = Recurrences.Zone(await db.Users.Where(u => u.Id == creatorId).Select(u => u.TimeZone).FirstAsync(ct));
        var comments = (await db.Comments.AsNoTracking()
                .Where(c => c.CardId == card.Id)
                .Select(c => new { Author = c.Author!.DisplayName, c.Body, c.CreatedAt })
                .ToListAsync(ct))
            .OrderByDescending(c => c.CreatedAt).Take(30).Reverse();
        var prompt = new StringBuilder()
            .Append("Title: ").Append(card.Title).Append('\n')
            .Append("Priority: ").Append(card.Priority).Append('\n')
            .Append("Due: ").Append(card.DueAt is { } due ? AiText.Day(due, zone) : "none").Append("\n\n")
            .Append("Description:\n").Append(AiText.Cut(card.Description, 4_000)).Append("\n\nComments (oldest first):\n");
        foreach (var comment in comments)
        {
            prompt.Append("- ").Append(comment.Author).Append(": ").Append(AiText.Cut(comment.Body, 600)).Append('\n');
        }

        var outcome = await gateway.RunAsync(
            AiPrompts.Summary, AiText.Cut(prompt.ToString(), AiText.MaxPromptChars), 300, text => AiDrafts.ParseText(text, MaxSummaryLength), ct);
        return outcome.Problem is { } p
            ? ([], Reason(p))
            : ([new ActionSpec(AutomationCatalog.DoComment, Text: "✨ " + outcome.Value!)], null);
    }

    private static string Reason(Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult problem) =>
        problem.StatusCode == StatusCodes.Status429TooManyRequests
            ? "The daily AI limit is spent."
            : "The AI answer couldn't be used.";

    private async Task SkipAsync(AiActionJob job, AutomationRule rule, Card card, string reason, CancellationToken ct)
    {
        var prefix = await db.Boards.AsNoTracking().Where(b => b.Id == job.BoardId).Select(b => b.KeyPrefix).FirstAsync(ct);
        var data = new JsonObject
        {
            ["rule"] = rule.Name,
            ["reason"] = reason,
            ["card"] = new JsonObject { ["key"] = Card.Key(prefix, card.Number), ["title"] = card.Title },
            [AutomationMarker.Property] = new JsonObject { ["ruleId"] = rule.Id, ["name"] = rule.Name },
        };
        db.Activity.Add(new ActivityEntry
        {
            Type = ActivityTypes.AutomationAiSkipped,
            BoardId = job.BoardId,
            CardId = card.Id,
            ActorId = job.TriggeredById,
            Data = data.ToJsonString(AutomationSpec.Json),
            CreatedAt = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
    }
}
