using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Profile.GetMe;
using ITask.Api.Features.Profile.UpdateProfile;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Profile;

public sealed class UpdateProfileTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task User_updates_name_time_zone_and_colour()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PutAsJsonAsync(
            "/api/me", new UpdateProfile.Request("  Grace Hopper ", "Europe/Amsterdam", "#0f766e"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var me = await client.GetFromJsonAsync<GetMe.Response>("/api/me");
        Assert.Equal("Grace Hopper", me!.DisplayName);
        Assert.Equal("Europe/Amsterdam", me.TimeZone);
        Assert.Equal("#0F766E", me.AvatarColor);
    }

    [Theory]
    [InlineData("", "UTC", "#1D5BF5")]
    [InlineData("Name", "Mars/Olympus", "#1D5BF5")]
    [InlineData("Name", "UTC", "blue")]
    public async Task Invalid_values_are_rejected(string name, string timeZone, string colour)
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.PutAsJsonAsync("/api/me", new UpdateProfile.Request(name, timeZone, colour));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_users_cannot_update()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.PutAsJsonAsync("/api/me", new UpdateProfile.Request("Name", "UTC", "#1D5BF5"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
