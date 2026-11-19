using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Admin.ListUsers;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Admin;

public sealed class DemoUserTypeTests(DemoApiFactory factory) : IClassFixture<DemoApiFactory>
{
    private static string Type(Guid userId) => $"/api/admin/users/{userId}/type";

    [Fact]
    public async Task The_demo_admin_cannot_change_user_types()
    {
        var demoAdmin = factory.CreateHttpsClient();
        (await demoAdmin.PostAsync(new Uri("/api/auth/demo/admin", UriKind.Relative), null)).EnsureSuccessStatusCode();
        var userId = await (await factory.CreateSignedInClientAsync()).GetUserIdAsync();

        var response = await demoAdmin.PutAsJsonAsync(Type(userId), new { type = "PlatformAdmin" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Demo_accounts_keep_their_type()
    {
        var admin = await factory.CreateAdminAsync();
        var users = await admin.GetFromJsonAsync<ListUsers.Response>("/api/admin/users?limit=100");
        var demoEditor = users!.Items.Single(u => u.Email == "demo-editor@example.com");

        var response = await admin.PutAsJsonAsync(Type(demoEditor.Id), new { type = "PlatformAdmin" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
