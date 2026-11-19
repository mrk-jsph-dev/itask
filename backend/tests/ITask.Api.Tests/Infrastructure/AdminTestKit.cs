using System.Net.Http.Json;
using ITask.Api.Common.Auth;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Infrastructure;

public static class AdminTestKit
{
    /// <summary>Makes <paramref name="email"/> a Platform admin directly in the database.</summary>
    public static async Task PromoteAsync(this ApiFactory factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var admins = scope.ServiceProvider.GetRequiredService<PlatformAdmins>();
        await admins.SetAsync((await users.FindByEmailAsync(email))!, admin: true, actorId: Guid.Empty);
    }

    /// <summary>A fresh sign-in, so the cookie carries the current role claims at once.</summary>
    public static async Task<HttpClient> SignInAsync(this ApiFactory factory, string email)
    {
        var client = factory.CreateHttpsClient();
        (await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = ApiFactory.Password }))
            .EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>A new account promoted to Platform admin and signed in.</summary>
    public static async Task<HttpClient> CreateAdminAsync(this ApiFactory factory)
    {
        var email = $"admin-{Guid.NewGuid():N}@example.com";
        await factory.CreateSignedInClientAsync(email);
        await factory.PromoteAsync(email);
        return await factory.SignInAsync(email);
    }
}
