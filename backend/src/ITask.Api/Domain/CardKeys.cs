namespace ITask.Api.Domain;

/// <summary>Card key prefixes: 2 to 5 uppercase ASCII letters, defaulted from the board name.</summary>
public static class CardKeys
{
    public const string Fallback = "ITK";

    public const int MinLength = 2;

    public const int MaxLength = 5;

    /// <summary>
    /// Initials of the name's words ("Website relaunch" → "WR"); a single word gives its first
    /// three letters ("Roadmap" → "ROA"). Only ASCII letters count; too few gives <see cref="Fallback"/>.
    /// </summary>
    public static string DefaultPrefix(string name)
    {
        var words = name
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string([.. w.Where(char.IsAsciiLetter)]).ToUpperInvariant())
            .Where(w => w.Length > 0)
            .ToList();

        var prefix = words.Count switch
        {
            0 => string.Empty,
            1 => words[0][..Math.Min(3, words[0].Length)],
            _ => new string([.. words.Take(MaxLength).Select(w => w[0])]),
        };

        return prefix.Length >= MinLength ? prefix : Fallback;
    }
}
