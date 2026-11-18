using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Auth.Demo;
using ITask.Api.Features.Profile.GetMe;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Auth;

public sealed class DemoTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    [Fact]
    public async Task Status_reports_enabled()
    {
        var client = factory.CreateHttpsClient();

        var status = await client.GetFromJsonAsync<GetDemoStatus.Response>("/api/auth/demo");

        Assert.True(status!.Enabled);
    }

    [Theory]
    [InlineData("admin", "Demo Admin", true)]
    [InlineData("owner", "Demo Owner", false)]
    [InlineData("editor", "Demo Editor", false)]
    [InlineData("viewer", "Demo Viewer", false)]
    public async Task Each_role_signs_in_without_a_password(string role, string name, bool admin)
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PostAsync(new Uri($"/api/auth/demo/{role}", UriKind.Relative), null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var me = await client.GetFromJsonAsync<GetMe.Response>("/api/me");
        Assert.Equal(name, me!.DisplayName);
        Assert.True(me.IsDemo);
        Assert.Equal(admin, me.IsPlatformAdmin);
    }

    [Fact]
    public async Task Unknown_role_is_not_found()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PostAsync(new Uri("/api/auth/demo/root", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Demo_user_cannot_change_password()
    {
        var client = factory.CreateHttpsClient();
        (await client.PostAsync(new Uri("/api/auth/demo/editor", UriKind.Relative), null)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync(
            "/api/me/password", new { currentPassword = "Demo-password-123!", newPassword = "Another-password-456!" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

public sealed class DemoDisabledTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Status_reports_disabled()
    {
        var client = factory.CreateHttpsClient();

        var status = await client.GetFromJsonAsync<GetDemoStatus.Response>("/api/auth/demo");

        Assert.False(status!.Enabled);
    }

    [Fact]
    public async Task Demo_sign_in_is_not_found()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PostAsync(new Uri("/api/auth/demo/editor", UriKind.Relative), null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
