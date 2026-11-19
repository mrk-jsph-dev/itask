using Microsoft.Azure.SignalR;

namespace ITask.Api.Common.Realtime;

public static class RealtimeExtensions
{
    /// <summary>
    /// SignalR in the API process (one replica). <c>Realtime:AzureSignalR=true</c> moves connections to
    /// Azure SignalR Service (<c>Azure:SignalR:ConnectionString</c>) for more replicas; presence then
    /// only sees each replica's own connections.
    /// </summary>
    public static IServiceCollection AddRealtime(this IServiceCollection services, IConfiguration config)
    {
        var signalR = services.AddSignalR();
        if (config.GetValue<bool>("Realtime:AzureSignalR"))
        {
            signalR.AddAzureSignalR();
        }

        services.AddSingleton<BoardPresence>();
        services.AddSingleton<IBoardEvents, BoardEvents>();
        services.AddScoped<BoardEventsInterceptor>();
        return services;
    }

    public static IEndpointRouteBuilder MapRealtime(this IEndpointRouteBuilder app)
    {
        app.MapHub<BoardHub>(BoardHub.Path).RequireAuthorization();
        return app;
    }
}
