using System.Text.Json;

namespace ITask.Api.Common.Activity;

public sealed record ActivityBoardRef(Guid Id, string Name, string? Emoji);

public sealed record ActivityActor(Guid Id, string DisplayName, string AvatarColor);

/// <summary>One feed entry. <c>Data</c> is the JSON recorded with the change.</summary>
public sealed record ActivityView(
    Guid Id,
    string Type,
    JsonElement Data,
    DateTimeOffset CreatedAt,
    Guid? CardId,
    ActivityBoardRef Board,
    ActivityActor Actor);

public sealed record ActivityPage(IReadOnlyList<ActivityView> Items, string? NextCursor);
