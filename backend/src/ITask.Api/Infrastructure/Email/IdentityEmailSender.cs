using ITask.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Email;

/// <summary>Identity's account emails (confirmation, password reset) sent through <see cref="IAppEmailSender"/>.</summary>
internal sealed class IdentityEmailSender(IAppEmailSender sender, IOptions<EmailOptions> options) : IEmailSender<AppUser>
{
    public Task SendConfirmationLinkAsync(AppUser user, string email, string confirmationLink) =>
        sender.SendAsync(EmailTemplates.Confirmation(email, confirmationLink), CancellationToken.None);

    public Task SendPasswordResetLinkAsync(AppUser user, string email, string resetLink) =>
        sender.SendAsync(EmailTemplates.PasswordReset(email, resetLink, null), CancellationToken.None);

    /// <summary>The Identity API sends a code; the email links to the site's reset page with it filled in.</summary>
    public Task SendPasswordResetCodeAsync(AppUser user, string email, string resetCode)
    {
        var link = options.Value.Link(
            $"reset-password?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(resetCode)}");
        return sender.SendAsync(EmailTemplates.PasswordReset(email, link, resetCode), CancellationToken.None);
    }
}
