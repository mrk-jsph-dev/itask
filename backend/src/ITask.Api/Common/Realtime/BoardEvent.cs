namespace ITask.Api.Common.Realtime;

/// <summary>
/// A change on a board, pushed to the board's live group. Ids and positions only: clients
/// fetch what they need through the API, so the event never carries names or text.
/// </summary>
/// <param name="Type">An activity type (<c>card.moved</c>, ...) or one of <see cref="LiveTypes"/>.</param>
/// <param name="ListId">The card's list after the change, for card events.</param>
/// <param name="Position">The card's position after the change, for card events.</param>
/// <param name="UserId">The member a <c>member.*</c> event is about.</param>
public sealed record BoardEvent(
    string Type,
    Guid BoardId,
    Guid? CardId,
    Guid? ListId,
    double? Position,
    Guid? UserId,
    Guid ActorId,
    DateTimeOffset At);

/// <summary>Live event types that have no activity entry.</summary>
public static class LiveTypes
{
    public const string BoardDeleted = "board.deleted";
}

/// <summary>Someone with the board open.</summary>
public sealed record PresenceUser(Guid UserId, string DisplayName, string AvatarColor);

/// <summary>Who has a board open now, sent on every join and leave.</summary>
public sealed record PresenceUpdate(Guid BoardId, IReadOnlyList<PresenceUser> Users);

/// <summary>Sent to a connection whose user lost access to the board it had open.</summary>
public sealed record AccessLost(Guid BoardId);
