using ITask.Api.Common.Auth;
using ITask.Api.Common.Cards;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Ai;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Automations;

/// <summary>An automation as the API returns it.</summary>
/// <param name="Timing"><c>instant</c> (runs inside the change that triggers it) or <c>hourly</c> (may run late while the app sleeps).</param>
/// <param name="Problem">Why the automation can't run as it is (it refers to something gone), or null.</param>
public sealed record AutomationView(
    Guid Id,
    AutomationKind Kind,
    string Name,
    Guid? ListId,
    DoorwayDirection? Direction,
    DoorwayMode? Mode,
    TriggerSpec Trigger,
    IReadOnlyList<ConditionSpec> Conditions,
    IReadOnlyList<ActionSpec> Actions,
    bool Enabled,
    string Timing,
    int Runs,
    DateTimeOffset? LastRunAt,
    string? Problem,
    DateTimeOffset CreatedAt,
    string RowVersion)
{
    /// <summary>Days of runs counted in <see cref="Runs"/>.</summary>
    public const int RunWindowDays = 30;

    public static AutomationView From(AutomationRule rule, BoardContext board, int runs = 0, DateTimeOffset? lastRunAt = null)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var spec = AutomationSpec.Of(rule);
        var trigger = rule.Kind == AutomationKind.Doorway && rule.ListId is { } listId && rule.Direction is { } direction
            ? AutomationSpec.DoorwayTrigger(listId, direction)
            : spec.Trigger;
        var problem = AutomationCatalog.Validate(rule.Kind, rule.ListId, rule.Direction, rule.Mode, spec, board)
            .SelectMany(e => e.Value)
            .FirstOrDefault();
        return new AutomationView(
            rule.Id, rule.Kind, rule.Name, rule.ListId, rule.Direction, rule.Mode, trigger, spec.Conditions, spec.Actions,
            rule.Enabled, AutomationCatalog.IsHourly(trigger.Type) ? "hourly" : "instant", runs, lastRunAt, problem,
            rule.CreatedAt, Convert.ToBase64String(rule.RowVersion));
    }
}

/// <summary>Access checks shared by the automation slices.</summary>
public static class AutomationGuard
{
    /// <summary>
    /// Null when the caller has at least <paramref name="minimum"/> on the board (and, for writes,
    /// the board is active); otherwise 404 for non-members, 403 below the role, 409 when archived.
    /// </summary>
    public static async Task<ProblemHttpResult?> CheckAsync(
        Guid boardId, BoardRole minimum, bool write, AppDbContext db, IBoardAccess access, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(access);
        var role = await access.GetRoleAsync(boardId, ct);
        if (role is null)
        {
            return CardGuard.NotFound();
        }

        if (role < minimum)
        {
            return BoardResults.Forbidden();
        }

        if (write && await db.Boards.AnyAsync(b => b.Id == boardId && b.ArchivedAt != null, ct))
        {
            return BoardResults.Archived();
        }

        return null;
    }

    /// <summary>
    /// Validates the parts of an automation for <paramref name="boardId"/>; a doorway's trigger is
    /// replaced by the one its list and direction imply. Returns the spec to store, or the errors.
    /// </summary>
    public static async Task<(AutomationSpec? Spec, Dictionary<string, string[]>? Errors)> ValidateAsync(
        AppDbContext db, Guid boardId, AutomationKind kind, Guid? listId, DoorwayDirection? direction, DoorwayMode? mode,
        TriggerSpec? trigger, IReadOnlyList<ConditionSpec>? conditions, IReadOnlyList<ActionSpec>? actions, bool aiAllowed,
        CancellationToken ct)
    {
        var spec = new AutomationSpec(
            kind == AutomationKind.Doorway && listId is { } id && direction is { } dir
                ? AutomationSpec.DoorwayTrigger(id, dir)
                : trigger ?? new TriggerSpec(""),
            conditions ?? [],
            actions ?? []);
        if (kind == AutomationKind.Rule && (listId is not null || direction is not null || mode is not null))
        {
            return (null, new Dictionary<string, string[]> { ["kind"] = ["Only doorways have a list, direction and mode."] });
        }

        if (!aiAllowed && spec.Actions.Any(a => AutomationCatalog.IsAi(a.Type)))
        {
            return (null, new Dictionary<string, string[]> { ["actions"] = ["✨ AI actions aren't available here (AI is off, or this is a demo account)."] });
        }

        var board = await AutomationBoards.LoadAsync(db, boardId, ct);
        var errors = AutomationCatalog.Validate(kind, listId, direction, mode, spec, board);
        return errors.Count > 0 ? (null, errors) : (spec, null);
    }

    /// <summary>Whether the signed-in user may add ✨ AI actions: AI is on and they aren't a demo account.</summary>
    public static async Task<bool> AiAllowedAsync(AppDbContext db, IAiService ai, ICurrentUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(ai);
        ArgumentNullException.ThrowIfNull(user);
        return ai.IsEnabled && !await db.Users.AnyAsync(u => u.Id == user.Id && u.IsDemo, ct);
    }
}
