using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the API in-process against a per-factory SQLite file that is deleted on dispose.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
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
        }));
    }

    public HttpClient CreateHttpsClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    /// <summary>Registers a fresh user and returns a client holding their auth cookie.</summary>
    public async Task<HttpClient> CreateSignedInClientAsync()
    {
        var client = CreateHttpsClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        const string password = "Test-password-123!";

        (await client.PostAsJsonAsync("/api/auth/register", new { email, password })).EnsureSuccessStatusCode();
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
