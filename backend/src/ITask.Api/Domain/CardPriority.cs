using System.Text.Json.Serialization;

namespace ITask.Api.Domain;

/// <summary>Card priority, ordered from lowest to highest. Serialized by name in JSON.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CardPriority>))]
public enum CardPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Urgent = 4,
}
