using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ITask.Api.Domain;

/// <summary>
/// A board automation. A <see cref="AutomationKind.Doorway"/> belongs to one list and direction and
/// checks requirements on cards passing through; a <see cref="AutomationKind.Rule"/> reacts to any
/// trigger on the board. Trigger, conditions and actions are JSON (see <c>Common/Automations/AutomationSpec</c>).
/// </summary>
public class AutomationRule
{
    public const int NameMaxLength = 80;

    public const int JsonMaxLength = 4_000;

    /// <summary>Most automations one board can hold.</summary>
    public const int MaxPerBoard = 25;

    /// <summary>Most actions one automation can run.</summary>
    public const int MaxActions = 3;

    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BoardId { get; set; }

    public Board? Board { get; set; }

    public AutomationKind Kind { get; set; }

    public required string Name { get; set; }

    /// <summary>The doorway's list. Not a foreign key; a missing or archived list makes the doorway inert.</summary>
    public Guid? ListId { get; set; }

    public DoorwayDirection? Direction { get; set; }

    public DoorwayMode? Mode { get; set; }

    public string Trigger { get; set; } = "{}";

    /// <summary>Filters for rules, requirements for doorways.</summary>
    public string Conditions { get; set; } = "[]";

    public string Actions { get; set; } = "[]";

    public bool Enabled { get; set; } = true;

    /// <summary>Who added it; the actor of hourly runs.</summary>
    public Guid CreatedById { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<AutomationKind>))]
public enum AutomationKind
{
    Rule,
    Doorway,
}

[JsonConverter(typeof(JsonStringEnumConverter<DoorwayDirection>))]
public enum DoorwayDirection
{
    Enter,
    Leave,
}

/// <summary>What a doorway does with a card that doesn't meet its requirements.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DoorwayMode>))]
public enum DoorwayMode
{
    /// <summary>The move is refused with the unmet requirements.</summary>
    Block,

    /// <summary>The move goes through and the gaps are filled in.</summary>
    Fix,

    /// <summary>The move goes through; the card shows a warning until the requirements are met.</summary>
    Warn,
}
