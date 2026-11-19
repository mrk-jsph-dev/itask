using ITask.Api.Common.Realtime;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Persistence;

public static class DatabaseExtensions
{
    /// <summary>
    /// Database:Provider picks the engine.
    ///  - "SqlServer" (default): Azure SQL or a local SQL Server container.
    ///  - "Sqlite": local development and tests; no server needed.
    /// </summary>
    /// <remarks>Configuration is read when the context is built, so test host overrides apply.</remarks>
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.AddInterceptors(sp.GetRequiredService<BoardEventsInterceptor>());
            var provider = config["Database:Provider"] ?? "SqlServer";
            var connectionString = config.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

            if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connectionString);
            }
            else
            {
                options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure());
            }
        });

        return services;
    }

    /// <summary>
    /// SQL Server applies the migrations. SQLite (development and tests) creates the schema
    /// from the model, because the migrations are generated for SQL Server.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Database:AutoMigrate"))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (db.Database.IsSqlite())
        {
            await db.Database.EnsureCreatedAsync();
        }
        else
        {
            // EF Core takes an sp_getapplock for the migration, so replicas starting together don't race.
            await db.Database.MigrateAsync();
        }
    }
}
