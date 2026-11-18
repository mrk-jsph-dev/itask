using ITask.Api.Infrastructure.Maintenance;
using ITask.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Maintenance;

internal static class MaintenanceTestKit
{
    /// <summary>Runs the job named <paramref name="name"/> directly, skipping the schedule.</summary>
    public static async Task RunJobAsync(this ApiFactory factory, string name, DateTimeOffset now)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetServices<IMaintenanceJob>().Single(j => j.Name == name);
        await job.RunAsync(now, CancellationToken.None);
    }

    public static async Task<IReadOnlyList<string>> RunDueAsync(this ApiFactory factory, DateTimeOffset now)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MaintenanceRunner>().RunDueAsync(now, CancellationToken.None);
    }
}
