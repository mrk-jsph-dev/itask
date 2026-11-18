using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Maintenance;

/// <summary>Checks for due maintenance jobs at startup and then every <see cref="MaintenanceOptions.CheckInterval"/>.</summary>
internal sealed partial class MaintenanceService(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<MaintenanceOptions> options,
    ILogger<MaintenanceService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.CheckInterval, time);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var runner = scope.ServiceProvider.GetRequiredService<MaintenanceRunner>();
                await runner.RunDueAsync(time.GetUtcNow(), stoppingToken);
            }
#pragma warning disable CA1031 // A paused or unreachable database is retried on the next tick.
            catch (Exception e) when (e is not OperationCanceledException)
#pragma warning restore CA1031
            {
                LogCheckFailed(logger, e);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Maintenance check failed; retrying on the next tick.")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
