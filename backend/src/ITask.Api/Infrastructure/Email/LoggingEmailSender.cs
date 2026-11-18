using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Infrastructure.Email;

/// <summary>
/// Writes Identity's account emails to the log instead of sending them. Links and codes
/// are logged only in Development so they never reach production logs.
/// </summary>
internal sealed partial class LoggingEmailSender(IHostEnvironment env, ILogger<LoggingEmailSender> logger)
    : IEmailSender<AppUser>
{
    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink)
        => Log("confirmation link", email, confirmationLink);

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink)
        => Log("password reset link", email, resetLink);

    public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode)
        => Log("password reset code", email, resetCode);

    private Task Log(string kind, string email, string payload)
    {
        if (env.IsDevelopment())
        {
            LogEmailWithPayload(logger, kind, email, payload);
        }
        else
        {
            LogEmail(logger, kind);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email ({Kind}) to {Email}: {Payload}")]
    private static partial void LogEmailWithPayload(ILogger logger, string kind, string email, string payload);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email ({Kind}) not sent: no email provider configured.")]
    private static partial void LogEmail(ILogger logger, string kind);
}
