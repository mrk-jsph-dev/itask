using System.Text;
using System.Text.Json;

namespace ITask.Api.Common.Automations;

/// <summary>An automation run the client is told about, so it can show it with Undo.</summary>
public sealed record AutomationRunNotice(Guid RunId, string RuleName, string Summary, bool CanUndo);

/// <summary>
/// Runs made during the current request. Runs from a save that fails are dropped; the rest go out
/// in the <see cref="Header"/> response header. One instance per scope.
/// </summary>
public sealed class AutomationRunLog
{
    public const string Header = "X-ITask-Automations";

    private readonly List<AutomationRunNotice> _pending = [];
    private readonly List<AutomationRunNotice> _saved = [];

    public IReadOnlyList<AutomationRunNotice> Saved => _saved;

    internal void Add(AutomationRunNotice notice) => _pending.Add(notice);

    internal void Commit()
    {
        _saved.AddRange(_pending);
        _pending.Clear();
    }

    internal void Discard() => _pending.Clear();

    /// <summary>Base64 of the saved runs as camelCase JSON (headers must be ASCII), or null when there are none.</summary>
    public string? HeaderValue() => _saved.Count == 0
        ? null
        : Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(_saved, AutomationSpec.Json)));
}

/// <summary>Adds <see cref="AutomationRunLog.Header"/> to API responses when the request ran automations.</summary>
public static class AutomationHeader
{
    public static async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var result = await next(context);
        var log = context.HttpContext.RequestServices.GetService<AutomationRunLog>();
        if (log?.HeaderValue() is { } value)
        {
            context.HttpContext.Response.Headers[AutomationRunLog.Header] = value;
        }

        return result;
    }
}

public static class AutomationExtensions
{
    public static IServiceCollection AddAutomations(this IServiceCollection services)
    {
        services.AddScoped<AutomationRunLog>();
        services.AddScoped<AutomationEngine>();
        services.AddScoped<AutomationInterceptor>();
        return services;
    }
}
