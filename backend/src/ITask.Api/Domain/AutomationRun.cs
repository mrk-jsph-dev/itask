using System.Text.Json.Serialization;

namespace ITask.Api.Domain;

/// <summary>
/// One time an automation acted on (or blocked) a card. <see cref="UndoJson"/> holds the changed
/// fields before and after the run, so the run can be undone while nothing else changed them.
/// </summary>
public class AutomationRun
{
    public const int KeyMaxLength = 120;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid RuleId { get; set; }

    public Guid BoardId { get; set; }

    public Guid CardId { get; set; }

    public Guid TriggeredById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set by scheduled triggers so a rerun can't act twice on the same occurrence.</summary>
    public string? Key { get; set; }

    public AutomationOutcome Outcome { get; set; }

    public string? UndoJson { get; set; }

    public DateTimeOffset? UndoneAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<AutomationOutcome>))]
public enum AutomationOutcome
{
    /// <summary>The automation changed the card (see <see cref="AutomationRun.UndoJson"/>).</summary>
    Ran,

    /// <summary>A block-mode doorway refused the move; nothing was changed.</summary>
    Blocked,

    /// <summary>A scheduled trigger found nothing to change; kept so the occurrence isn't checked again.</summary>
    Skipped,
}
