using System.Text.Json.Serialization;

namespace ITask.Api.Domain;

/// <summary>Colour theme the user picked: follow the operating system, or force light or dark.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
public enum ThemePreference
{
    System,
    Light,
    Dark,
}
