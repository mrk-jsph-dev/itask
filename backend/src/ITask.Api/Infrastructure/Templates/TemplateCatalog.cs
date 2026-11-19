using System.Text.Json;
using ITask.Api.Common.Templates;

namespace ITask.Api.Infrastructure.Templates;

/// <summary>A template shipped with the app (<c>Infrastructure/Templates/*.json</c>).</summary>
public sealed record BuiltInTemplate(string Id, string Name, string Emoji, string Description, string Color, TemplateContent Content)
{
    public TemplateSummary Summary() => TemplateSummary.Of(Id, TemplateSummary.BuiltIn, Name, Emoji, Description, Color, Content);
}

public interface ITemplateCatalog
{
    /// <summary>Built-in templates in gallery order.</summary>
    IReadOnlyList<BuiltInTemplate> All { get; }

    BuiltInTemplate? Find(string id);
}

/// <summary>Reads the embedded template files once.</summary>
internal sealed class TemplateCatalog : ITemplateCatalog
{
    /// <summary>Gallery order; files not listed here follow alphabetically.</summary>
    private static readonly string[] Order = ["kanban", "sprint", "personal", "bug-tracker", "cloud-ops"];

    private const string Prefix = "ITask.Api.Infrastructure.Templates.";

    public TemplateCatalog()
    {
        var assembly = typeof(TemplateCatalog).Assembly;
        All = [.. assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n =>
            {
                using var stream = assembly.GetManifestResourceStream(n)!;
                return JsonSerializer.Deserialize<BuiltInTemplate>(stream, TemplateContent.Json)
                    ?? throw new JsonException($"Template {n} is empty.");
            })
            .OrderBy(t => Array.IndexOf(Order, t.Id) is var i && i >= 0 ? i : Order.Length)
            .ThenBy(t => t.Id, StringComparer.Ordinal)];
    }

    public IReadOnlyList<BuiltInTemplate> All { get; }

    public BuiltInTemplate? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
