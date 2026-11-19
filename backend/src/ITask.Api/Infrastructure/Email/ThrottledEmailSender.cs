using System.Security.Cryptography;
using System.Text;
using ITask.Api.Common.Security;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Email;

/// <summary>
/// Puts daily limits in front of the real sender: <see cref="EmailOptions.DailyLimit"/> emails for the
/// whole app and <see cref="EmailOptions.DailyPerRecipient"/> per address. Emails over a limit are
/// dropped with a warning, never queued, so callers behave the same either way.
/// </summary>
internal sealed partial class ThrottledEmailSender(
    IAppEmailSender inner,
    IServiceScopeFactory scopes,
    IOptions<EmailOptions> options,
    ILogger<ThrottledEmailSender> logger) : IAppEmailSender
{
    public bool CanSend => inner.CanSend;

    public static ThrottledEmailSender Over<TSender>(IServiceProvider services)
        where TSender : IAppEmailSender
        => new(
            services.GetRequiredService<TSender>(),
            services.GetRequiredService<IServiceScopeFactory>(),
            services.GetRequiredService<IOptions<EmailOptions>>(),
            services.GetRequiredService<ILogger<ThrottledEmailSender>>());

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);

        await using (var scope = scopes.CreateAsyncScope())
        {
            var counters = scope.ServiceProvider.GetRequiredService<DailyCounters>();
            var limits = options.Value;
            if (!await counters.TryIncrementAsync(RecipientCounter(message.To), limits.DailyPerRecipient, ct))
            {
                LogRecipientLimit(logger, message.Subject);
                return;
            }

            if (!await counters.TryIncrementAsync(DailyCounters.Email, limits.DailyLimit, ct))
            {
                LogDailyLimit(logger, message.Subject);
                return;
            }
        }

        await inner.SendAsync(message, ct);
    }

    /// <summary>The address is hashed so the counters table never holds emails.</summary>
    internal static string RecipientCounter(string address) =>
        $"{DailyCounters.Email}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(address.Trim().ToUpperInvariant())))}";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email \"{Subject}\" dropped: the recipient reached today's limit.")]
    private static partial void LogRecipientLimit(ILogger logger, string subject);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Email \"{Subject}\" dropped: the app reached today's email limit.")]
    private static partial void LogDailyLimit(ILogger logger, string subject);
}
