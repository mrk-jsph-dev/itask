using System.Net;
using System.Net.Http.Json;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Maintenance;
using ITask.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ITask.Api.Tests.Features.Auth;

public sealed class EmailOtpTests(OtpApiFactory factory) : IClassFixture<OtpApiFactory>
{
    private static string NewEmail() => $"otp-{Guid.NewGuid():N}@example.com";

    private async Task<(HttpClient Client, string Email)> RegisterAsync()
    {
        var client = factory.CreateHttpsClient();
        var email = NewEmail();
        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new { displayName = "New User", email, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (client, email);
    }

    private static Task<HttpResponseMessage> VerifyAsync(HttpClient client, string email, string code) =>
        client.PostAsJsonAsync("/api/auth/verify-email", new { email, code });

    [Fact]
    public async Task Register_emails_a_code_and_the_account_cannot_sign_in_until_verified()
    {
        var (client, email) = await RegisterAsync();

        Assert.Matches("^[0-9]{6}$", factory.LatestCode(email));
        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Contains("NotAllowed", await login.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_right_code_confirms_the_email_and_signs_in()
    {
        var (client, email) = await RegisterAsync();

        var verified = await VerifyAsync(client, email, factory.LatestCode(email));

        Assert.Equal(HttpStatusCode.OK, verified.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(new Uri("/api/me", UriKind.Relative))).StatusCode);
        var again = await VerifyAsync(factory.CreateHttpsClient(), email, factory.LatestCode(email));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        var login = await factory.CreateHttpsClient()
            .PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Five_wrong_codes_void_the_code()
    {
        var (client, email) = await RegisterAsync();
        var code = factory.LatestCode(email);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var i = 0; i < EmailCodes.MaxAttempts; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, email, wrong)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, email, code)).StatusCode);
    }

    [Fact]
    public async Task Codes_expire_and_resend_respects_the_cooldown()
    {
        var (client, email) = await RegisterAsync();
        var first = factory.LatestCode(email);

        (await client.PostAsJsonAsync("/api/auth/resend-code", new { email })).EnsureSuccessStatusCode();
        Assert.Single(factory.Emails.To(email));

        factory.Clock.Advance(EmailCodes.Lifetime + TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, email, first)).StatusCode);

        (await client.PostAsJsonAsync("/api/auth/resend-code", new { email })).EnsureSuccessStatusCode();
        Assert.Equal(2, factory.Emails.To(email).Count);
        Assert.Equal(HttpStatusCode.OK, (await VerifyAsync(client, email, factory.LatestCode(email))).StatusCode);
    }

    [Fact]
    public async Task Resend_is_capped_per_day_and_says_nothing_about_unknown_emails()
    {
        var (client, email) = await RegisterAsync();
        for (var i = 0; i < EmailCodes.MaxSendsPerDay + 2; i++)
        {
            factory.Clock.Advance(EmailCodes.ResendCooldown);
            (await client.PostAsJsonAsync("/api/auth/resend-code", new { email })).EnsureSuccessStatusCode();
        }

        Assert.Equal(EmailCodes.MaxSendsPerDay, factory.Emails.To(email).Count);
        var unknown = await client.PostAsJsonAsync("/api/auth/resend-code", new { email = NewEmail() });
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
    }

    [Fact]
    public async Task Identity_resend_confirmation_route_sends_a_code_not_a_link()
    {
        var (client, email) = await RegisterAsync();
        factory.Clock.Advance(EmailCodes.ResendCooldown);

        (await client.PostAsJsonAsync("/api/auth/resendConfirmationEmail", new { email })).EnsureSuccessStatusCode();

        Assert.All(factory.Emails.To(email), m => Assert.EndsWith("is your iTask code", m.Subject, StringComparison.Ordinal));
        Assert.Equal(2, factory.Emails.To(email).Count);
    }

    [Fact]
    public async Task Unknown_email_and_bad_format_are_rejected_the_same_way()
    {
        var client = factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, NewEmail(), "123456")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await VerifyAsync(client, NewEmail(), "12ab")).StatusCode);
    }

    [Fact]
    public async Task Sign_ups_stop_at_the_daily_cap()
    {
        var today = DateOnly.FromDateTime(factory.Clock.GetUtcNow().UtcDateTime);
        Task SetSignupsAsync(int count) => factory.WithDbAsync(async db =>
        {
            await db.UsageCounters.Where(c => c.Name == DailyCounters.Signups && c.Date == today).ExecuteDeleteAsync();
            db.UsageCounters.Add(new UsageCounter { Name = DailyCounters.Signups, Date = today, Count = count });
            await db.SaveChangesAsync();
        });
        await SetSignupsAsync(1000);

        try
        {
            var response = await factory.CreateHttpsClient().PostAsJsonAsync(
                "/api/auth/register", new { displayName = "Late", email = NewEmail(), password = ApiFactory.Password });

            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        finally
        {
            await SetSignupsAsync(0);
        }
    }

    [Fact]
    public async Task Stale_unverified_accounts_are_deleted_and_others_kept()
    {
        var (_, stale) = await RegisterAsync();
        var (fresh, verified) = await RegisterAsync();
        (await VerifyAsync(fresh, verified, factory.LatestCode(verified))).EnsureSuccessStatusCode();
        var (_, recent) = await RegisterAsync();
        await factory.WithDbAsync(db => db.Users.Where(u => u.Email == stale || u.Email == verified)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, DateTimeOffset.UtcNow.AddDays(-2))));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var job = scope.ServiceProvider.GetServices<IMaintenanceJob>().Single(j => j.Name == "unverified-accounts");
            await job.RunAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        }

        await factory.WithDbAsync(async db =>
        {
            var left = await db.Users.Select(u => u.Email).ToListAsync();
            Assert.DoesNotContain(stale, left);
            Assert.Contains(verified, left);
            Assert.Contains(recent, left);
        });
    }
}
