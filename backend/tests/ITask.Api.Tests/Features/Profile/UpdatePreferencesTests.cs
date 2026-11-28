using System.Net;
using System.Net.Http.Json;
using ITask.Api.Domain;
using ITask.Api.Features.Profile.GetMe;
using ITask.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Features.Profile;

public sealed class UpdatePreferencesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task New_users_follow_the_system_theme()
    {
        var client = await factory.CreateSignedInClientAsync();

        var me = await client.GetFromJsonAsync<GetMe.Response>("/api/me");

        Assert.Equal(ThemePreference.System, me!.ThemePreference);
    }

    [Fact]
    public async Task User_sets_dark_theme_and_it_comes_back_from_me()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PatchAsJsonAsync("/api/me", new { themePreference = "Dark" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("Dark", body!["themePreference"]);

        var me = await client.GetFromJsonAsync<GetMe.Response>("/api/me");
        Assert.Equal(ThemePreference.Dark, me!.ThemePreference);
    }

    [Fact]
    public async Task Absent_fields_keep_their_value()
    {
        var client = await factory.CreateSignedInClientAsync();
        await client.PatchAsJsonAsync("/api/me", new { themePreference = "Light" });

        var response = await client.PatchAsJsonAsync("/api/me", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await client.GetFromJsonAsync<GetMe.Response>("/api/me");
        Assert.Equal(ThemePreference.Light, me!.ThemePreference);
    }

    [Theory]
    [InlineData("\"Sepia\"")]
    [InlineData("7")]
    public async Task Unknown_themes_are_rejected(string value)
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PatchAsync(
            new Uri("/api/me", UriKind.Relative),
            new StringContent($"{{\"themePreference\":{value}}}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Demo_accounts_cannot_save_preferences()
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

        var response = await client.PatchAsJsonAsync("/api/me", new { themePreference = "Dark" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_users_get_401()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PatchAsJsonAsync("/api/me", new { themePreference = "Dark" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
