using System.Net;
using System.Net.Http.Json;
using ITask.Api.Features.Admin.ListUsers;
using ITask.Api.Features.Profile.GetMe;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Admin;

public sealed class UserTypeTests(AdminEmailsApiFactory factory) : IClassFixture<AdminEmailsApiFactory>
{
    private static string Type(Guid userId) => $"/api/admin/users/{userId}/type";

    [Fact]
    public async Task A_listed_email_registers_as_platform_admin_and_others_as_users()
    {
        var listed = await factory.CreateSignedInClientAsync(AdminEmailsApiFactory.AdminEmail);
        var regular = await factory.CreateSignedInClientAsync();

        Assert.True((await listed.GetFromJsonAsync<GetMe.Response>("/api/me"))!.IsPlatformAdmin);
        Assert.False((await regular.GetFromJsonAsync<GetMe.Response>("/api/me"))!.IsPlatformAdmin);
    }

    [Fact]
    public async Task Admin_promotes_a_user_who_then_manages_users_and_demotes_them_again()
    {
        var admin = await factory.CreateAdminAsync();
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var user = await factory.CreateSignedInClientAsync(email);
        var userId = await user.GetUserIdAsync();

        var promote = await admin.PutAsJsonAsync(Type(userId), new { type = "PlatformAdmin" });
        var again = await admin.PutAsJsonAsync(Type(userId), new { type = "PlatformAdmin" });
        var promoted = await factory.SignInAsync(email);

        Assert.Equal(HttpStatusCode.NoContent, promote.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.True((await promoted.GetFromJsonAsync<GetMe.Response>("/api/me"))!.IsPlatformAdmin);
        var list = await promoted.GetFromJsonAsync<ListUsers.Response>("/api/admin/users?limit=100");
        Assert.Contains(list!.Items, u => u.Id == userId && u.IsPlatformAdmin);

        var demote = await admin.PutAsJsonAsync(Type(userId), new { type = "User" });
        var demoted = await factory.SignInAsync(email);

        Assert.Equal(HttpStatusCode.NoContent, demote.StatusCode);
        Assert.False((await demoted.GetFromJsonAsync<GetMe.Response>("/api/me"))!.IsPlatformAdmin);
        Assert.Equal(HttpStatusCode.Forbidden, (await demoted.GetAsync(new Uri("/api/admin/users", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Admins_cannot_change_their_own_type()
    {
        var admin = await factory.CreateAdminAsync();
        var adminId = await admin.GetUserIdAsync();

        var response = await admin.PutAsJsonAsync(Type(adminId), new { type = "User" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True((await admin.GetFromJsonAsync<GetMe.Response>("/api/me"))!.IsPlatformAdmin);
    }

    [Fact]
    public async Task Regular_users_are_forbidden_and_unknown_users_not_found()
    {
        var admin = await factory.CreateAdminAsync();
        var user = await factory.CreateSignedInClientAsync();
        var userId = await user.GetUserIdAsync();

        var forbidden = await user.PutAsJsonAsync(Type(userId), new { type = "PlatformAdmin" });
        var missing = await admin.PutAsJsonAsync(Type(Guid.NewGuid()), new { type = "PlatformAdmin" });
        var invalid = await admin.PutAsJsonAsync(Type(userId), new { type = 99 });

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Signed_out_callers_are_unauthorized()
    {
        var response = await factory.CreateHttpsClient()
            .PutAsJsonAsync(Type(Guid.NewGuid()), new { type = "PlatformAdmin" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
