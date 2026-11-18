using System.Net;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Auth;

public sealed class LogoutTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var client = await factory.CreateSignedInClientAsync();

        var logout = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var me = await client.GetAsync(new Uri("/api/me", UriKind.Relative));
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Anonymous_logout_is_unauthorized()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PostAsync(new Uri("/api/auth/logout", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
