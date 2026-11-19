using System.Text.RegularExpressions;

namespace ITask.Api.Infrastructure.Ai;

/// <summary>A system prompt from <c>Prompts/{Name}.md</c>; <see cref="Version"/> comes from its <c>&lt;!-- vN --&gt;</c> first line.</summary>
public sealed record AiPrompt(string Name, int Version, string Text);

/// <summary>The embedded system prompts, loaded once.</summary>
public sealed partial class AiPrompts
{
    public const string QuickAdd = "quick-add";

    public const string Breakdown = "breakdown";

    public const string Summary = "summary";

    public const string Digest = "digest";

    public const string BoardFromGoal = "board-from-goal";

    public const string Automation = "automation";

    public const string AutomationReview = "automation-review";

    private const string Prefix = "ITask.Api.Infrastructure.Ai.Prompts.";

    private readonly Dictionary<string, AiPrompt> _prompts;

    public AiPrompts()
    {
        var assembly = typeof(AiPrompts).Assembly;
        _prompts = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal))
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                using var reader = new StreamReader(stream);
                return Parse(n[Prefix.Length..^".md".Length], reader.ReadToEnd());
            })
            .ToDictionary(p => p.Name, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<AiPrompt> All => _prompts.Values;

    public AiPrompt Get(string name) => _prompts.TryGetValue(name, out var prompt)
        ? prompt
        : throw new KeyNotFoundException($"No AI prompt named '{name}'.");

    internal static AiPrompt Parse(string name, string content)
    {
        var match = VersionLine().Match(content);
        if (!match.Success)
        {
            throw new FormatException($"AI prompt '{name}' must start with a <!-- vN --> version line.");
        }

        return new AiPrompt(name, int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), content[match.Length..].Trim());
    }

    [GeneratedRegex(@"\A\s*<!--\s*v(\d+)\s*-->")]
    private static partial Regex VersionLine();
}
