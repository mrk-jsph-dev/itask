using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Admin.ListUsers;
using ITask.Api.Features.Admin.LockUser;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Admin;

public sealed class AdminUsersTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Regular_users_are_forbidden()
    {
        var client = await factory.CreateSignedInClientAsync();

        var response = await client.GetAsync(new Uri("/api/admin/users", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_pages_through_users()
    {
        var admin = await factory.CreateAdminAsync();
        await factory.CreateSignedInClientAsync();
        await factory.CreateSignedInClientAsync();

        var first = await admin.GetFromJsonAsync<ListUsers.Response>("/api/admin/users?limit=2");
        var second = await admin.GetFromJsonAsync<ListUsers.Response>($"/api/admin/users?limit=2&cursor={first!.NextCursor}");

        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        Assert.NotEmpty(second!.Items);
        Assert.DoesNotContain(second.Items, u => first.Items.Any(f => f.Id == u.Id));
        Assert.Contains(first.Items.Concat(second.Items), u => u.IsPlatformAdmin);
    }

    [Fact]
    public async Task Locked_users_cannot_sign_in_until_unlocked()
    {
        var admin = await factory.CreateAdminAsync();
        var email = $"locked-{Guid.NewGuid():N}@example.com";
        var victim = await factory.CreateSignedInClientAsync(email);
        var victimId = await victim.GetUserIdAsync();

        var lockResponse = await admin.PutAsJsonAsync($"/api/admin/users/{victimId}/lock", new LockUser.Request(true));
        var login = await factory.CreateHttpsClient()
            .PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = ApiFactory.Password });

        Assert.Equal(HttpStatusCode.NoContent, lockResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        await admin.PutAsJsonAsync($"/api/admin/users/{victimId}/lock", new LockUser.Request(false));
        var relogin = await factory.CreateHttpsClient()
            .PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    [Fact]
    public async Task Admins_cannot_lock_themselves()
    {
        var admin = await factory.CreateAdminAsync();
        var adminId = await admin.GetUserIdAsync();

        var response = await admin.PutAsJsonAsync($"/api/admin/users/{adminId}/lock", new LockUser.Request(true));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
