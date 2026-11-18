using System.Net;
using System.Net.Http.Json;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Auth;

public sealed class TurnstileTests(RejectingTurnstileApiFactory factory) : IClassFixture<RejectingTurnstileApiFactory>
{
    [Fact]
    public async Task Register_requires_a_valid_token()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { displayName = "Bot", email = "bot@example.com", password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_requires_a_valid_token()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true", new { email = "bot@example.com", password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
