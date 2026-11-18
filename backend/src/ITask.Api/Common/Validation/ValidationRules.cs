using System.Text.RegularExpressions;
using FluentValidation;

namespace ITask.Api.Common.Validation;

public static partial class ValidationRules
{
    /// <summary>A #RRGGBB hex colour.</summary>
    public static IRuleBuilderOptions<T, string?> HexColor<T>(this IRuleBuilder<T, string?> rule)
        => rule.Matches(HexColorPattern()).WithMessage("Use a #RRGGBB colour.");

    /// <summary>A base64 row version as returned by the API.</summary>
    public static IRuleBuilderOptions<T, string> RowVersion<T>(this IRuleBuilder<T, string> rule)
        => rule.NotEmpty().Must(v => TryDecode(v, out _)).WithMessage("Invalid row version.");

    public static bool TryDecode(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var buffer = new byte[value.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written))
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColorPattern();
}
