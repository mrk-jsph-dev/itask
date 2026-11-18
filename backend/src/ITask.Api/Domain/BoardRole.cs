using System.Text.Json.Serialization;

namespace ITask.Api.Domain;

/// <summary>
/// Role of a user on one board. Values are ordered so <c>role &gt;= minimum</c> checks work:
/// Viewer reads and comments, Editor also changes lists and cards, Owner also manages
/// members, settings and integrations.
/// Serialized by name ("Owner") in JSON.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BoardRole>))]
public enum BoardRole
{
    Viewer = 0,
    Editor = 1,
    Owner = 2,
}
