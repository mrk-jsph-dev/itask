using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Profile.ChangePassword;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Profile;

public sealed class ChangePasswordTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string NewPassword = "Brand-new-password-456!";

    [Fact]
    public async Task New_password_works_for_the_next_sign_in()
    {
        var email = $"pw-{Guid.NewGuid():N}@example.com";
        var client = await factory.CreateSignedInClientAsync(email);

        var change = await client.PostAsJsonAsync("/api/me/password", new ChangePassword.Request(ApiFactory.Password, NewPassword));
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        var fresh = factory.CreateHttpsClient();
        var oldLogin = await fresh.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var newLogin = await fresh.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
    }

    [Fact]
    public async Task Wrong_current_password_is_rejected()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/me/password", new ChangePassword.Request("Not-my-password-1!", NewPassword));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_users_cannot_change_password()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PostAsJsonAsync("/api/me/password", new ChangePassword.Request(ApiFactory.Password, NewPassword));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
