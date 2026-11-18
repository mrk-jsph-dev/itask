using System.Net;
using System.Net.Http.Json;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Security;

public sealed class CrossSiteApiFactory : ApiFactory
{
    public const string Site = "https://site.example.com";

    protected override Dictionary<string, string?> ExtraConfig => new()
    {
        ["Cors:AllowedOrigins:0"] = Site,
        ["Auth:CookieSameSite"] = "None",
    };
}

public sealed class OriginGuardTests(CrossSiteApiFactory factory) : IClassFixture<CrossSiteApiFactory>
{
    private static HttpRequestMessage CreateBoard(string? origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/boards") { Content = JsonContent.Create(new { name = "Board" }) };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return request;
    }

    [Fact]
    public async Task Foreign_origin_write_is_forbidden()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.SendAsync(CreateBoard("https://evil.example.net"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(CrossSiteApiFactory.Site)]
    [InlineData(null)]
    public async Task Allowed_or_missing_origin_writes(string? origin)
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.SendAsync(CreateBoard(origin));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Reads_ignore_the_origin()
    {
        var client = await factory.CreateSignedInClientAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/boards");
        request.Headers.Add("Origin", "https://evil.example.net");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cross_site_cookie_is_none_and_partitioned()
    {
        var client = factory.CreateHttpsClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/auth/register", new { displayName = "Test User", email, password = ApiFactory.Password }))
            .EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = ApiFactory.Password });

        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("itask.auth=", StringComparison.Ordinal));
        Assert.Contains("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("partitioned", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
    }
}
