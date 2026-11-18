namespace ITask.Api.Common.Cards;

/// <summary>Finds <c>@DisplayName</c> mentions of board members in comment text.</summary>
public static class Mentions
{
    /// <summary>
    /// Ids of the members whose display name follows an <c>@</c> in <paramref name="body"/>, matched
    /// case-insensitively and only when the name ends at a non-letter/digit. Longer names win, so
    /// "@Ada Lovelace" mentions "Ada Lovelace" and not "Ada".
    /// </summary>
    public static IReadOnlyList<Guid> Parse(string body, IEnumerable<(Guid UserId, string DisplayName)> members)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(members);

        var candidates = members
            .Where(m => !string.IsNullOrWhiteSpace(m.DisplayName))
            .OrderByDescending(m => m.DisplayName.Length)
            .ToList();
        var found = new List<Guid>();

        for (var at = body.IndexOf('@', StringComparison.Ordinal); at >= 0; at = body.IndexOf('@', at + 1))
        {
            // An @ inside a word (e.g. an email address) is not a mention.
            if (at > 0 && char.IsLetterOrDigit(body[at - 1]))
            {
                continue;
            }

            var rest = body.AsSpan(at + 1);
            foreach (var (userId, name) in candidates)
            {
                if (rest.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                    && (rest.Length == name.Length || !char.IsLetterOrDigit(rest[name.Length])))
                {
                    if (!found.Contains(userId))
                    {
                        found.Add(userId);
                    }

                    break;
                }
            }
        }

        return found;
    }
}
