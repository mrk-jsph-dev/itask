using System.Collections.Concurrent;
using ITask.Api.Infrastructure.Ai;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>An <see cref="ApiFactory"/> with AI on, a daily cap of <see cref="DailyCap"/> and <see cref="FakeAiService"/> instead of Foundry.</summary>
public sealed class AiApiFactory : ApiFactory
{
    public const int DailyCap = 5;

    public FakeAiService Ai { get; } = new();

    protected override Dictionary<string, string?> ExtraConfig => new()
    {
        ["Ai:Enabled"] = "true",
        ["Ai:DailyRequestsPerUser"] = DailyCap.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["RateLimits:AiPerMinute"] = "1000",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IAiService>(Ai));
}

/// <summary>Answers with <see cref="Respond"/> and records every request.</summary>
public sealed class FakeAiService : IAiService
{
    public ConcurrentQueue<AiRequest> Requests { get; } = new();

    public Func<AiRequest, string> Respond { get; set; } = _ => "{}";

    public bool Fail { get; set; }

    public bool IsEnabled => true;

    public Task<AiResult> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        if (Fail)
        {
            throw new HttpRequestException("Foundry is down.");
        }

        return Task.FromResult(new AiResult(Respond(request), 100, 20));
    }

    /// <summary>The user prompt of the latest request.</summary>
    public string LastPrompt => Requests.Last().User;
}
