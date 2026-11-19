using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Notifications;
using ITask.Api.Domain;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Notifications;

public sealed class PreferencesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Url = "/api/me/notification-preferences";

    [Fact]
    public async Task Defaults_have_the_digest_off_and_every_type_on()
    {
        var client = await factory.CreateSignedInClientAsync();

        var prefs = (await client.GetFromJsonAsync<NotificationPreferencesView>(Url))!;

        Assert.Equal(DigestFrequency.Off, prefs.Digest);
        Assert.Equal(new DigestTypes(true, true, true, true, true, true), prefs.Types);
        Assert.False(prefs.EmailConfirmed);
    }

    [Fact]
    public async Task Saves_and_reads_back()
    {
        var client = await factory.CreateSignedInClientAsync();
        var types = new DigestTypes(Mentioned: true, Assigned: false, DueSoon: true, Overdue: false, Invited: true, CommentOnMyCard: false);

        var response = await client.PutAsJsonAsync(Url, new { digest = "Weekly", types });

        response.EnsureSuccessStatusCode();
        var saved = (await client.GetFromJsonAsync<NotificationPreferencesView>(Url))!;
        Assert.Equal((DigestFrequency.Weekly, types), (saved.Digest, saved.Types));
    }

    [Fact]
    public async Task Unknown_frequency_or_missing_types_is_400()
    {
        var client = await factory.CreateSignedInClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Url, new { digest = 9, types = new { } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync(Url, new { digest = "Daily" })).StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateHttpsClient().GetAsync(new Uri(Url, UriKind.Relative))).StatusCode);
    }
}
