using System.Net;
using System.Net.Http.Json;
using ITask.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace ITask.Api.Tests.Features.Auth;

/// <summary>Pins the failure details the sign-in page maps to messages.</summary>
public sealed class LoginTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static async Task<(HttpStatusCode Status, string? Detail)> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password });
        if (response.IsSuccessStatusCode)
        {
            return (response.StatusCode, null);
        }

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        return (response.StatusCode, problem?.Detail);
    }

    private async Task<(HttpClient Client, string Email)> RegisterAsync()
    {
        var client = factory.CreateHttpsClient();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        (await client.PostAsJsonAsync("/api/auth/register", new { displayName = "Test User", email, password = ApiFactory.Password }))
            .EnsureSuccessStatusCode();
        return (client, email);
    }

    [Fact]
    public async Task Wrong_password_is_failed()
    {
        var (client, email) = await RegisterAsync();

        var (status, detail) = await LoginAsync(client, email, "Wrong-password-1");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("Failed", detail);
    }

    [Fact]
    public async Task Five_failures_lock_the_account_even_for_the_right_password()
    {
        var (client, email) = await RegisterAsync();
        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(client, email, "Wrong-password-1");
        }

        var (status, detail) = await LoginAsync(client, email, ApiFactory.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("LockedOut", detail, ignoreCase: true);
    }
}
