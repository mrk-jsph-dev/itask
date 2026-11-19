namespace ITask.Api.Infrastructure.Email;

/// <summary>A ready-to-send email: plain text plus an HTML alternative.</summary>
public sealed record EmailMessage(string To, string Subject, string Text, string Html);

/// <summary>Sends the app's emails (account, invites, digests).</summary>
public interface IAppEmailSender
{
    /// <summary>True when emails really leave the app (an SMTP account is configured).</summary>
    bool CanSend { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct);
}
