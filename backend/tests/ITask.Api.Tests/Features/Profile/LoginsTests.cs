using System.Net;
using System.Net.Http.Json;
using ITask.Api.Domain;
using ITask.Api.Features.Auth.CompleteExternal;
using ITask.Api.Features.Profile.ListLogins;
using ITask.Api.Features.Profile.SetPassword;
using ITask.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Features.Profile;

public sealed class LoginsTests(ExternalLoginFactory factory) : IClassFixture<ExternalLoginFactory>
{
    private const string NewPassword = "Brand-new-password-456!";

    /// <summary>A user created through GitHub, so it has no password.</summary>
    private async Task<HttpClient> GitHubOnlyClientAsync()
    {
        var client = factory.CreateHttpsClient(allowAutoRedirect: false);
        var nonce = Guid.NewGuid().ToString("N");
        var query = await ExternalLoginFactory.CallbackAsync(client, new FakeExternalIdentity(
            "GitHub", Guid.NewGuid().ToString("N"), nonce, $"gh-{Guid.NewGuid():N}@example.com", "Octo", "octo"));
        (await client.PostAsJsonAsync("/api/auth/external/complete", new CompleteExternal.Request(query["code"], nonce)))
            .EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Lists_password_logins_and_providers()
    {
        var client = await GitHubOnlyClientAsync();

        var logins = await client.GetFromJsonAsync<ListLogins.Response>("/api/me/logins");

        Assert.False(logins!.HasPassword);
        Assert.Equal("github", Assert.Single(logins.Logins).Provider);
        Assert.Equal("octo", logins.Logins[0].AccountName);
        Assert.Equal(["github", "microsoft"], logins.Providers);
    }

    [Fact]
    public async Task Last_sign_in_method_cannot_be_removed_until_a_password_is_set()
    {
        var client = await GitHubOnlyClientAsync();

        var blocked = await client.DeleteAsync(new Uri("/api/me/logins/github", UriKind.Relative));
        var set = await client.PostAsJsonAsync("/api/me/password/set", new SetPassword.Request(NewPassword));
        var removed = await client.DeleteAsync(new Uri("/api/me/logins/github", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        var logins = await client.GetFromJsonAsync<ListLogins.Response>("/api/me/logins");
        Assert.True(logins!.HasPassword);
        Assert.Empty(logins.Logins);
    }

    [Fact]
    public async Task Set_password_is_refused_when_one_exists()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PostAsJsonAsync("/api/me/password/set", new SetPassword.Request(NewPassword));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Set_password_validates_strength()
    {
        var client = await GitHubOnlyClientAsync();

        var response = await client.PostAsJsonAsync("/api/me/password/set", new SetPassword.Request("short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Removing_a_provider_that_is_not_connected_is_not_found()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.DeleteAsync(new Uri("/api/me/logins/microsoft", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("gitlab", HttpStatusCode.NotFound)]
    [InlineData("github", HttpStatusCode.Conflict)]
    public async Task Start_link_rejects_unknown_or_connected_providers(string provider, HttpStatusCode expected)
    {
        var client = await GitHubOnlyClientAsync();

        var response = await client.PostAsync(new Uri($"/api/me/logins/{provider}/link", UriKind.Relative), null);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Demo_accounts_cannot_link_or_unlink()
    {
        var email = $"demo-{Guid.NewGuid():N}@example.com";
        var client = await factory.CreateSignedInClientAsync(email);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByEmailAsync(email);
            user!.IsDemo = true;
            await users.UpdateAsync(user);
        }

        var link = await client.PostAsync(new Uri("/api/me/logins/github/link", UriKind.Relative), null);
        var unlink = await client.DeleteAsync(new Uri("/api/me/logins/github", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, link.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unlink.StatusCode);
    }

    [Fact]
    public async Task Anonymous_users_get_401()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.GetAsync(new Uri("/api/me/logins", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
