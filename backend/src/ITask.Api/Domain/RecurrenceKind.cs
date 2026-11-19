using System.Text.Json.Serialization;

namespace ITask.Api.Domain;

/// <summary>How often a recurring card repeats. Serialized by name in JSON.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RecurrenceKind>))]
public enum RecurrenceKind
{
    Daily = 0,

    /// <summary>On the days in <see cref="CardRecurrence.Weekdays"/>.</summary>
    Weekly = 1,

    /// <summary>On <see cref="CardRecurrence.DayOfMonth"/>, or the month's last day when it is shorter.</summary>
    Monthly = 2,
}
