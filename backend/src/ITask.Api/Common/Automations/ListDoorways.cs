using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Automations;

/// <summary>A block-mode doorway that refused a card, with the requirements it didn't meet.</summary>
public sealed record DoorwayBlock(AutomationRule Rule, string ListName, bool Leaving, IReadOnlyList<string> Unmet)
{
    /// <summary>409 ProblemDetails with <c>code: "doorway"</c> and the unmet requirements.</summary>
    public ProblemHttpResult ToProblem() => TypedResults.Problem(
        title: Leaving
            ? $"Cards leaving {ListName} need {string.Join(", ", Unmet)} first."
            : $"{ListName} needs {string.Join(", ", Unmet)} first.",
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = "doorway",
            ["ruleId"] = Rule.Id,
            ["rule"] = Rule.Name,
            ["unmet"] = Unmet,
        });
}

/// <summary>Checks block-mode doorways before a card is put in or taken out of a list.</summary>
public static class ListDoorways
{
    /// <summary>
    /// The first block-mode doorway <paramref name="card"/> fails when it goes from
    /// <paramref name="fromListId"/> (null for a new card) to <paramref name="toListId"/>, or null.
    /// Doorways that refer to something gone are inert.
    /// </summary>
    public static async Task<DoorwayBlock?> CheckAsync(
        AppDbContext db, Card card, Guid? fromListId, Guid toListId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(card);
        if (fromListId == toListId)
        {
            return null;
        }

        var doorways = await db.AutomationRules.AsNoTracking()
            .Where(r => r.BoardId == card.BoardId && r.Enabled && r.Kind == AutomationKind.Doorway && r.Mode == DoorwayMode.Block
                && ((r.ListId == toListId && r.Direction == DoorwayDirection.Enter)
                    || (fromListId != null && r.ListId == fromListId && r.Direction == DoorwayDirection.Leave)))
            .ToListAsync(ct);
        if (doorways.Count == 0)
        {
            return null;
        }

        var board = await AutomationBoards.LoadAsync(db, card.BoardId, ct);
        var state = fromListId is null ? CardState.Empty(card) : await CardState.LoadAsync(db, card, ct);
        foreach (var rule in doorways.OrderBy(r => r.CreatedAt))
        {
            var spec = AutomationSpec.Of(rule);
            if (AutomationCatalog.Validate(rule.Kind, rule.ListId, rule.Direction, rule.Mode, spec, board).Count > 0)
            {
                continue;
            }

            var unmet = spec.Conditions.Where(c => !state.Meets(c)).Select(c => CardState.Describe(c, board)).ToList();
            if (unmet.Count > 0)
            {
                return new DoorwayBlock(rule, board.Lists[rule.ListId!.Value], rule.Direction == DoorwayDirection.Leave, unmet);
            }
        }

        return null;
    }
}
