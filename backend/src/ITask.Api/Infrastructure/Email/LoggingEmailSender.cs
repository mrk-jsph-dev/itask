namespace ITask.Api.Infrastructure.Email;

/// <summary>
/// Writes emails to the log instead of sending them. Recipients and bodies (which hold links
/// and codes) are logged only in Development so they never reach production logs.
/// </summary>
internal sealed partial class LoggingEmailSender(IHostEnvironment env, ILogger<LoggingEmailSender> logger) : IAppEmailSender
{
    public bool CanSend => false;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (env.IsDevelopment())
        {
            LogEmailWithBody(logger, message.Subject, message.To, message.Text);
        }
        else
        {
            LogEmail(logger, message.Subject);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Email \"{Subject}\" to {To}:\n{Body}")]
    private static partial void LogEmailWithBody(ILogger logger, string subject, string to, string body);

    [LoggerMessage(Level = LogLevel.Information, Message = "Email \"{Subject}\" not sent: no email provider configured.")]
    private static partial void LogEmail(ILogger logger, string subject);
}
