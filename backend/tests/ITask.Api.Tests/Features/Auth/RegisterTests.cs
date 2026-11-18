using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Profile.GetMe;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Auth;

public sealed class RegisterTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Registered_user_can_sign_in_and_read_their_profile()
    {
        var client = factory.CreateHttpsClient();
        var email = $"ada-{Guid.NewGuid():N}@example.com";

        var register = await client.PostAsJsonAsync(
            "/api/auth/register", new { displayName = "Ada Lovelace", email, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login?useCookies=true", new { email, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await client.GetFromJsonAsync<GetMe.Response>("/api/me");
        Assert.Equal("Ada Lovelace", me!.DisplayName);
        Assert.Equal(email, me.Email);
        Assert.Equal("UTC", me.TimeZone);
        Assert.False(me.IsDemo);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        var client = factory.CreateHttpsClient();
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        var body = new { displayName = "First", email, password = ApiFactory.Password };
        (await client.PostAsJsonAsync("/api/auth/register", body)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/auth/register", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Weak_password_is_rejected()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { displayName = "Weak", email = $"w-{Guid.NewGuid():N}@example.com", password = "short" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_display_name_is_rejected()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { displayName = "", email = $"n-{Guid.NewGuid():N}@example.com", password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_still_builds()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.GetAsync(new Uri("/openapi/v1.json", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
