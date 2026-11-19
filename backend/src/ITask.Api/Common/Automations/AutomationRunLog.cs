using System.Text;
using System.Text.Json;

namespace ITask.Api.Common.Automations;

/// <summary>An automation run the client is told about, so it can show it with Undo.</summary>
public sealed record AutomationRunNotice(Guid RunId, string RuleName, string Summary, bool CanUndo);

/// <summary>
/// Runs made during the current request. Runs from a save that fails are dropped; the rest go out
/// in the <see cref="Header"/> response header. One instance per scope.
/// </summary>
public sealed partial class AutomationRunLog(AutomationAiJobs queue, ILogger<AutomationRunLog> logger)
{
    public const string Header = "X-ITask-Automations";

    private readonly List<AutomationRunNotice> _pending = [];
    private readonly List<AutomationRunNotice> _saved = [];
    private readonly List<AiActionJob> _pendingAi = [];

    public IReadOnlyList<AutomationRunNotice> Saved => _saved;

    /// <summary>True once the current save queued an AI action; a save queues at most one.</summary>
    internal bool HasPendingAi => _pendingAi.Count > 0;

    internal void Add(AutomationRunNotice notice) => _pending.Add(notice);

    internal void AddAi(AiActionJob job) => _pendingAi.Add(job);

    /// <summary>The save went through: report its runs and hand its AI actions to the queue.</summary>
    internal void Commit()
    {
        _saved.AddRange(_pending);
        _pending.Clear();
        foreach (var job in _pendingAi)
        {
            if (!queue.TryAdd(job))
            {
                LogQueueFull(logger, job.RuleId);
            }
        }

        _pendingAi.Clear();
    }

    internal void Discard()
    {
        _pending.Clear();
        _pendingAi.Clear();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "AI action of automation {RuleId} dropped: the queue is full.")]
    private static partial void LogQueueFull(ILogger logger, Guid ruleId);

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
        services.AddSingleton<AutomationAiJobs>();
        services.AddScoped<AutomationAiRunner>();
        services.AddHostedService<AutomationAiWorker>();
        return services;
    }
}
