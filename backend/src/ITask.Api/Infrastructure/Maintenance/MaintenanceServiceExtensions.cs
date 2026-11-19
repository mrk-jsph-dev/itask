using ITask.Api.Infrastructure.Recurrence;

namespace ITask.Api.Infrastructure.Maintenance;

public static class MaintenanceServiceExtensions
{
    public static IServiceCollection AddMaintenance(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MaintenanceOptions>(config.GetSection(MaintenanceOptions.Section));
        services.AddScoped<IMaintenanceJob, DemoResetJob>();
        services.AddScoped<IMaintenanceJob, ActivityRetentionJob>();
        services.AddScoped<IMaintenanceJob, OrphanUploadsJob>();
        services.AddScoped<IMaintenanceJob, UnverifiedAccountsJob>();
        services.AddScoped<IMaintenanceJob, RecurringCardsJob>();
        services.AddScoped<MaintenanceRunner>();
        services.AddHostedService<MaintenanceService>();
        return services;
    }
}
