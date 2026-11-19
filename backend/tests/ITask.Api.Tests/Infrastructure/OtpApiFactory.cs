using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ITask.Api.Infrastructure.Email;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>
/// An <see cref="ApiFactory"/> that requires a confirmed email to sign in (as in production), keeps sent
/// emails in <see cref="Emails"/> and runs on <see cref="Clock"/>.
/// </summary>
public sealed class OtpApiFactory : ApiFactory
{
    public CapturingEmailSender Emails { get; } = new();

    public TestClock Clock { get; } = new();

    protected override Dictionary<string, string?> ExtraConfig => new()
    {
        ["Auth:RequireConfirmedEmail"] = "true",
        ["Auth:DailySignups"] = "1000",
    };

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IAppEmailSender>(Emails));
        services.AddSingleton<TimeProvider>(Clock);
    }

    /// <summary>The six-digit code in the latest sign-up email to <paramref name="email"/>.</summary>
    public string LatestCode(string email)
    {
        var subject = Emails.To(email).Last(m => m.Subject.EndsWith("is your iTask code", StringComparison.Ordinal)).Subject;
        return subject[..6];
    }
}
