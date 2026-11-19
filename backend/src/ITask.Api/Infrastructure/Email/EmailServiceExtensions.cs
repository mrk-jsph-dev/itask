using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Infrastructure.Email;

public static class EmailServiceExtensions
{
    /// <summary>SMTP when <c>Email:Smtp:Username</c> and <c>Password</c> are set, otherwise log-only.</summary>
    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration config)
    {
        var section = config.GetSection(EmailOptions.Section);
        services.Configure<EmailOptions>(section);

        if (section.Get<EmailOptions>()?.Smtp.IsConfigured == true)
        {
            services.AddSingleton<IAppEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IAppEmailSender, LoggingEmailSender>();
        }

        services.AddSingleton<IEmailSender<AppUser>, IdentityEmailSender>();
        return services;
    }
}
