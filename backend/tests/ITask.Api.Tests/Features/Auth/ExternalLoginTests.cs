using System.Net;
using System.Net.Http.Json;
using ITask.Api.Domain;
using ITask.Api.Features.Auth.CompleteExternal;
using ITask.Api.Features.Auth.ExternalProviders;
using ITask.Api.Features.Profile.GetMe;
using ITask.Api.Features.Profile.ListLogins;
using ITask.Api.Features.Profile.StartLink;
using ITask.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Features.Auth;

public sealed class ExternalLoginTests(ExternalLoginFactory factory) : IClassFixture<ExternalLoginFactory>
{
    private const string GitHub = "GitHub";
    private const string Microsoft = "Microsoft";

    private static string NewNonce() => Guid.NewGuid().ToString("N");

    private static string NewEmail() => $"ext-{Guid.NewGuid():N}@example.com";

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, string code, string nonce) =>
        client.PostAsJsonAsync("/api/auth/external/complete", new CompleteExternal.Request(code, nonce));

    /// <summary>Runs callback + complete on a fresh client and returns it signed in.</summary>
    private async Task<HttpClient> SignInExternalAsync(string scheme, string key, string? email, string? name = "Octo Cat")
    {
        var client = factory.CreateHttpsClient(allowAutoRedirect: false);
        var nonce = NewNonce();
        var query = await ExternalLoginFactory.CallbackAsync(client, new FakeExternalIdentity(scheme, key, nonce, email, name, key));
        Assert.True(query.ContainsKey("code"), $"Expected a code, got {string.Join(',', query)}");
        (await CompleteAsync(client, query["code"], nonce)).EnsureSuccessStatusCode();
        return client;
    }

    private async Task UpdateUserAsync(string email, Action<AppUser> change)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException("No such user.");
        change(user);
        Assert.True((await users.UpdateAsync(user)).Succeeded);
    }

    [Fact]
    public async Task Providers_lists_the_configured_ones()
    {
        var response = await factory.CreateHttpsClient().GetFromJsonAsync<ExternalProviders.Response>("/api/auth/providers");

        Assert.Equal(["github", "microsoft"], response!.Providers);
    }

    [Fact]
    public async Task Challenge_redirects_to_the_provider()
    {
        var client = factory.CreateHttpsClient(allowAutoRedirect: false);

        var response = await client.GetAsync(new Uri($"/api/auth/external/github?nonce={NewNonce()}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://github.com/login/oauth/authorize", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/auth/external/gitlab?nonce=0123456789abcdef0123", HttpStatusCode.NotFound)]
    [InlineData("/api/auth/external/github?nonce=short", HttpStatusCode.BadRequest)]
    [InlineData("/api/auth/external/github?nonce=0123456789abcdef0123&link=forged", HttpStatusCode.BadRequest)]
    public async Task Challenge_rejects_bad_input(string url, HttpStatusCode expected)
    {
        var response = await factory.CreateHttpsClient(allowAutoRedirect: false).GetAsync(new Uri(url, UriKind.Relative));

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task New_user_signs_up_with_a_verified_email_and_no_password()
    {
        var email = NewEmail();
        var client = await SignInExternalAsync(GitHub, Guid.NewGuid().ToString("N"), email, name: "Mona Lisa");

        var me = await client.GetFromJsonAsync<GetMe.Response>("/api/me");
        Assert.Equal(email, me!.Email);
        Assert.Equal("Mona Lisa", me.DisplayName);
        Assert.False(me.HasPassword);
    }

    [Fact]
    public async Task Returning_login_signs_in_the_same_user()
    {
        var key = Guid.NewGuid().ToString("N");
        var first = await SignInExternalAsync(Microsoft, key, NewEmail());
        var firstMe = await first.GetFromJsonAsync<GetMe.Response>("/api/me");

        var second = await SignInExternalAsync(Microsoft, key, email: null);
        var secondMe = await second.GetFromJsonAsync<GetMe.Response>("/api/me");

        Assert.Equal(firstMe!.Id, secondMe!.Id);
    }

    [Fact]
    public async Task Unverified_email_is_rejected()
    {
        var client = factory.CreateHttpsClient(allowAutoRedirect: false);

        var query = await ExternalLoginFactory.CallbackAsync(
            client, new FakeExternalIdentity(GitHub, Guid.NewGuid().ToString("N"), NewNonce()));

        Assert.Equal("email_unverified", query["error"]);
    }

    [Fact]
    public async Task Existing_unconfirmed_account_is_not_linked_by_email()
    {
        var email = NewEmail();
        await factory.CreateSignedInClientAsync(email);
        var client = factory.CreateHttpsClient(allowAutoRedirect: false);

        var query = await ExternalLoginFactory.CallbackAsync(
            client, new FakeExternalIdentity(GitHub, Guid.NewGuid().ToString("N"), NewNonce(), email));

        Assert.Equal("account_exists", query["error"]);
    }

    [Fact]
    public async Task Existing_confirmed_account_is_linked_by_verified_email()
    {
        var email = NewEmail();
        var passwordClient = await factory.CreateSignedInClientAsync(email);
        var passwordMe = await passwordClient.GetFromJsonAsync<GetMe.Response>("/api/me");
        await UpdateUserAsync(email, u => u.EmailConfirmed = true);

        var client = await SignInExternalAsync(GitHub, Guid.NewGuid().ToString("N"), email);

        var me = await client.GetFromJsonAsync<GetMe.Response>("/api/me");
        Assert.Equal(passwordMe!.Id, me!.Id);
        Assert.True(me.HasPassword);
    }

    [Fact]
    public async Task Code_needs_the_same_nonce_and_works_once()
    {
        var client = factory.CreateHttpsClient(allowAutoRedirect: false);
        var nonce = NewNonce();
        var query = await ExternalLoginFactory.CallbackAsync(
            client, new FakeExternalIdentity(GitHub, Guid.NewGuid().ToString("N"), nonce, NewEmail()));

        var otherBrowser = await CompleteAsync(factory.CreateHttpsClient(), query["code"], NewNonce());
        var first = await CompleteAsync(client, query["code"], nonce);
        var again = await CompleteAsync(client, query["code"], nonce);

        Assert.Equal(HttpStatusCode.BadRequest, otherBrowser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        var client = factory.CreateHttpsClient(allowAutoRedirect: false);
        var nonce = NewNonce();
        var query = await ExternalLoginFactory.CallbackAsync(
            client, new FakeExternalIdentity(GitHub, Guid.NewGuid().ToString("N"), nonce, NewEmail()));

        factory.Clock.Advance(TimeSpan.FromMinutes(3));
        var response = await CompleteAsync(client, query["code"], nonce);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Locked_user_cannot_complete()
    {
        var email = NewEmail();
        var key = Guid.NewGuid().ToString("N");
        await SignInExternalAsync(GitHub, key, email);
        await UpdateUserAsync(email, u =>
        {
            u.LockoutEnabled = true;
            u.LockoutEnd = DateTimeOffset.MaxValue;
        });

        var client = factory.CreateHttpsClient(allowAutoRedirect: false);
        var nonce = NewNonce();
        var query = await ExternalLoginFactory.CallbackAsync(client, new FakeExternalIdentity(GitHub, key, nonce));
        var response = await CompleteAsync(client, query["code"], nonce);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Signed_in_user_links_a_provider()
    {
        var client = await factory.CreateSignedInClientAsync(allowAutoRedirect: false);
        var ticket = await StartLinkAsync(client, "microsoft");
        var nonce = NewNonce();

        var query = await ExternalLoginFactory.CallbackAsync(
            client, new FakeExternalIdentity(Microsoft, Guid.NewGuid().ToString("N"), nonce, AccountName: "me@outlook.com", LinkTicket: ticket));
        var response = await CompleteAsync(client, query["code"], nonce);

        Assert.Equal("link", (await response.Content.ReadFromJsonAsync<CompleteExternal.Response>())!.Mode);
        var logins = await client.GetFromJsonAsync<ListLogins.Response>("/api/me/logins");
        var login = Assert.Single(logins!.Logins);
        Assert.Equal("microsoft", login.Provider);
        Assert.Equal("me@outlook.com", login.AccountName);
    }

    [Fact]
    public async Task Link_code_only_works_for_the_user_who_started_it()
    {
        var owner = await factory.CreateSignedInClientAsync(allowAutoRedirect: false);
        var ticket = await StartLinkAsync(owner, "github");
        var nonce = NewNonce();
        var query = await ExternalLoginFactory.CallbackAsync(
            owner, new FakeExternalIdentity(GitHub, Guid.NewGuid().ToString("N"), nonce, LinkTicket: ticket));

        var someoneElse = await factory.CreateSignedInClientAsync();
        var response = await CompleteAsync(someoneElse, query["code"], nonce);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_owned_by_another_user_cannot_be_linked()
    {
        var key = Guid.NewGuid().ToString("N");
        await SignInExternalAsync(GitHub, key, NewEmail());
        var client = await factory.CreateSignedInClientAsync(allowAutoRedirect: false);
        var ticket = await StartLinkAsync(client, "github");

        var query = await ExternalLoginFactory.CallbackAsync(
            client, new FakeExternalIdentity(GitHub, key, NewNonce(), LinkTicket: ticket));

        Assert.Equal("login_taken", query["error"]);
    }

    private static async Task<string> StartLinkAsync(HttpClient client, string provider)
    {
        var response = await client.PostAsync(new Uri($"/api/me/logins/{provider}/link", UriKind.Relative), null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<StartLink.Response>())!.Ticket;
    }
}

public sealed class ExternalLoginDisabledTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task No_providers_without_configuration()
    {
        var response = await factory.CreateHttpsClient().GetFromJsonAsync<ExternalProviders.Response>("/api/auth/providers");

        Assert.Empty(response!.Providers);
    }

    [Fact]
    public async Task Challenge_is_not_found_without_configuration()
    {
        var response = await factory.CreateHttpsClient(allowAutoRedirect: false)
            .GetAsync(new Uri("/api/auth/external/github?nonce=0123456789abcdef0123", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
