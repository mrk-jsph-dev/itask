using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Automations;

/// <summary>What one automation did over a period: changes it made and moves it blocked.</summary>
public sealed record AutomationTally(Guid RuleId, string Name, int Ran, int Blocked);

/// <summary>One board's automation receipt.</summary>
public sealed record BoardReceipt(Guid BoardId, string BoardName, int Ran, int Blocked, IReadOnlyList<AutomationTally> Rules)
{
    /// <summary>"Automations made 23 changes; doorways blocked 4 moves."</summary>
    public string Sentence()
    {
        var parts = new List<string>();
        if (Ran > 0)
        {
            parts.Add($"automations made {Ran} change{(Ran == 1 ? "" : "s")}");
        }

        if (Blocked > 0)
        {
            parts.Add($"doorways blocked {Blocked} move{(Blocked == 1 ? "" : "s")}");
        }

        var text = string.Join("; ", parts);
        return text.Length == 0 ? "" : char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }
}

/// <summary>Counts automation runs from <see cref="AutomationRun"/> rows (kept 30 days).</summary>
public static class AutomationReceipts
{
    public static async Task<List<BoardReceipt>> ForBoardsAsync(
        AppDbContext db, IReadOnlyCollection<Guid> boardIds, DateTimeOffset since, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (boardIds.Count == 0)
        {
            return [];
        }

        var runs = await db.AutomationRuns.AsNoTracking()
            .Where(r => boardIds.Contains(r.BoardId) && r.CreatedAt >= since && r.Outcome != AutomationOutcome.Skipped && r.UndoneAt == null)
            .Select(r => new { r.BoardId, r.RuleId, r.Outcome })
            .ToListAsync(ct);
        if (runs.Count == 0)
        {
            return [];
        }

        var ruleIds = runs.Select(r => r.RuleId).Distinct().ToList();
        var names = await db.AutomationRules.AsNoTracking().Where(r => ruleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        var boardNames = await db.Boards.AsNoTracking().Where(b => boardIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        return runs
            .GroupBy(r => r.BoardId)
            .Select(board => new BoardReceipt(
                board.Key,
                boardNames.GetValueOrDefault(board.Key, "A board"),
                board.Count(r => r.Outcome == AutomationOutcome.Ran),
                board.Count(r => r.Outcome == AutomationOutcome.Blocked),
                [.. board.GroupBy(r => r.RuleId)
                    .Select(rule => new AutomationTally(
                        rule.Key,
                        names.GetValueOrDefault(rule.Key, "A deleted automation"),
                        rule.Count(r => r.Outcome == AutomationOutcome.Ran),
                        rule.Count(r => r.Outcome == AutomationOutcome.Blocked)))
                    .OrderByDescending(t => t.Ran + t.Blocked)]))
            .OrderByDescending(r => r.Ran + r.Blocked)
            .ToList();
    }
}
