using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Infrastructure.Email;

public static class EmailServiceExtensions
{
    /// <summary>
    /// SMTP when <c>Email:Smtp:Username</c> and <c>Password</c> are set, otherwise log-only; either way behind
    /// <see cref="ThrottledEmailSender"/>'s daily limits.
    /// </summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(EmailOptions.Section);
        services.Configure<EmailOptions>(section);

        if (section.Get<EmailOptions>()?.Smtp.IsConfigured == true)
        {
            services.AddSingleton<SmtpEmailSender>();
            services.AddSingleton<IAppEmailSender>(sp => ThrottledEmailSender.Over<SmtpEmailSender>(sp));
        }
        else
        {
            services.AddSingleton<LoggingEmailSender>();
            services.AddSingleton<IAppEmailSender>(sp => ThrottledEmailSender.Over<LoggingEmailSender>(sp));
        }

        services.AddSingleton<IEmailSender<AppUser>, IdentityEmailSender>();
        return services;
    }
}
