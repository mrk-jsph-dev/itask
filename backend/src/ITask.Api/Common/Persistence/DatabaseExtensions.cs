using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Common.Persistence;

public static class DatabaseExtensions
{
    /// <summary>
    /// Database:Provider picks the engine.
    ///  - "SqlServer" (default): Azure SQL or a local SQL Server container.
    ///  - "Sqlite": local development and tests; no server needed.
    /// </summary>
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration config)
    {
        var provider = config["Database:Provider"] ?? "SqlServer";
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(options =>
        {
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
    /// Applies migrations when they exist; before the first migration is added it
    /// creates the schema directly (development only).
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Database:AutoMigrate"))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync();
        }
        else if (app.Environment.IsDevelopment())
        {
            await db.Database.EnsureCreatedAsync();
        }
    }
}
