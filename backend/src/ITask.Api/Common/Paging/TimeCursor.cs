using System.Globalization;

namespace ITask.Api.Common.Paging;

/// <summary>
/// Keyset cursor for newest-first feeds: the last item's creation time (UTC ticks) and id,
/// as <c>{ticks}_{id:N}</c>. Pages stay stable while new items are added.
/// </summary>
public static class TimeCursor
{
    public const int DefaultLimit = 50;

    public const int MaxLimit = 100;

    public static int Take(int? limit) => Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

    public static string Format(DateTimeOffset createdAt, Guid id) =>
        $"{createdAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}_{id:N}";

    public static bool TryParse(string cursor, out DateTimeOffset createdAt, out Guid id)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        createdAt = default;
        id = default;
        var parts = cursor.Split('_');
        if (parts.Length != 2
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTimeOffset.MaxValue.UtcTicks
            || !Guid.TryParseExact(parts[1], "N", out id))
        {
            return false;
        }

        createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }
}
