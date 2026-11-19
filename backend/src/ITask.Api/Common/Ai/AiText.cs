using System.Globalization;

namespace ITask.Api.Common.Ai;

/// <summary>Helpers for building prompt input from board data.</summary>
public static class AiText
{
    /// <summary>Most characters of board data sent in one prompt.</summary>
    public const int MaxPromptChars = 12_000;

    /// <summary>Trims and cuts <paramref name="text"/> to <paramref name="max"/> characters, marking the cut with an ellipsis.</summary>
    public static string Cut(string? text, int max)
    {
        var trimmed = (text ?? "").Trim();
        return trimmed.Length <= max ? trimmed : string.Concat(trimmed.AsSpan(0, max), "…");
    }

    public static string Day(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("yyyy-MM-dd ddd", CultureInfo.InvariantCulture);
}
