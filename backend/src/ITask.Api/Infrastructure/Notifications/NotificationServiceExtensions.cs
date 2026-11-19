using ITask.Api.Common.Notifications;
using ITask.Api.Infrastructure.Maintenance;

namespace ITask.Api.Infrastructure.Notifications;

public static class NotificationServiceExtensions
{
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        services.AddScoped<INotifier, Notifier>();
        services.AddScoped<IMaintenanceJob, DueRemindersJob>();
        services.AddScoped<IMaintenanceJob, NotificationDigestJob>();
        services.AddScoped<IMaintenanceJob, NotificationRetentionJob>();
        return services;
    }
}
