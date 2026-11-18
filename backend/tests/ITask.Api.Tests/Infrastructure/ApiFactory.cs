using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the API in-process against a per-factory SQLite file that is deleted on dispose.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"itask-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:AutoMigrate"] = "true",
            ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
            ["Demo:Enabled"] = "false",
            ["Auth:RequireConfirmedEmail"] = "false",
            ["RateLimits:AuthPerMinute"] = "1000",
        }).AddInMemoryCollection(ExtraConfig));
        builder.ConfigureTestServices(ConfigureServices);
    }

    /// <summary>Configuration layered over the test defaults.</summary>
    protected virtual Dictionary<string, string?> ExtraConfig => [];

    /// <summary>Replaces services after the app registered its own.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public HttpClient CreateHttpsClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    public const string Password = "Test-password-123!";

    /// <summary>Registers a fresh user and returns a client holding their auth cookie.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync(string? email = null)
    {
        var client = CreateHttpsClient();
        email ??= $"user-{Guid.NewGuid():N}@example.com";
        const string password = Password;

        (await client.PostAsJsonAsync("/api/auth/register", new { displayName = "Test User", email, password }))
            .EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password })).EnsureSuccessStatusCode();

        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
