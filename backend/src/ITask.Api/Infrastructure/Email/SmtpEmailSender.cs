using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ITask.Api.Infrastructure.Email;

/// <summary>Sends through an SMTP account (Gmail with an app password by default).</summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IAppEmailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public bool CanSend => true;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var o = options.Value;
        var from = string.IsNullOrWhiteSpace(o.FromAddress) ? o.Smtp.Username : o.FromAddress;

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        var security = o.Smtp.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
        await client.ConnectAsync(o.Smtp.Host, o.Smtp.Port, security, ct);
        await client.AuthenticateAsync(o.Smtp.Username, o.Smtp.Password, ct);
        await client.SendAsync(new SendRequest(mime), ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
