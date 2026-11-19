using System.Net;
using System.Text;

namespace ITask.Api.Infrastructure.Email;

/// <summary>One line of a digest: the sentence and an optional link to open it.</summary>
public sealed record DigestLine(string Text, string? Link);

/// <summary>
/// The app's emails as plain text plus a small HTML version. Everything that came from users
/// is HTML-encoded before it goes into the HTML part.
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage Confirmation(string to, string link) => Build(
        to,
        "Confirm your iTask email",
        "Confirm your email address to finish setting up your iTask account.",
        [],
        ("Confirm email", link),
        "If you didn't create an account, you can ignore this email.",
        list: false);

    /// <param name="link">The reset page with the code filled in, when the site URL is known.</param>
    /// <param name="code">Shown as text too, so the email works without a link.</param>
    public static EmailMessage PasswordReset(string to, string? link, string? code) => Build(
        to,
        "Reset your iTask password",
        "Someone asked to reset the password for this iTask account.",
        code is null ? [] : Plain($"Reset code: {code}"),
        link is null ? null : ("Choose a new password", link),
        "If it wasn't you, ignore this email: your password stays the same.",
        list: false);

    public static EmailMessage Invite(string to, string inviter, string board, string role, string? link) => Build(
        to,
        $"{inviter} invited you to {board} on iTask",
        $"{inviter} invited you to the board \"{board}\" as {role.ToLowerInvariant()}.",
        link is null ? Plain("Open iTask and sign in with this email to accept.") : [],
        link is null ? null : ("Accept invite", link),
        "The invite expires in 7 days.",
        list: false);

    public static EmailMessage Digest(string to, string name, IReadOnlyList<DigestLine> lines, string period, string? link)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return Build(
            to,
            $"Your iTask {period} digest: {lines.Count} new",
            $"Hi {name}, here is what needs you.",
            lines,
            link is null ? null : ("Open notifications", link),
            "You get this because you turned on the email digest. Change it in your iTask profile.",
            list: true);
    }

    private static DigestLine[] Plain(params string[] lines) => [.. lines.Select(l => new DigestLine(l, null))];

    private static EmailMessage Build(
        string to,
        string subject,
        string intro,
        IReadOnlyList<DigestLine> lines,
        (string Label, string Url)? action,
        string footer,
        bool list)
    {
        var text = new StringBuilder().AppendLine(intro).AppendLine();
        foreach (var line in lines)
        {
            text.AppendLine(list ? "- " + line.Text : line.Text);
            if (line.Link is not null)
            {
                text.Append("  ").AppendLine(line.Link);
            }
        }

        if (action is { } a)
        {
            text.AppendLine().Append(a.Label).Append(": ").AppendLine(a.Url);
        }

        text.AppendLine().AppendLine(footer);

        var html = new StringBuilder()
            .Append("<div style=\"font-family:Inter,Segoe UI,Arial,sans-serif;color:#111827;max-width:560px;margin:0 auto;padding:24px\">")
            .Append("<p style=\"font-size:18px;font-weight:700;margin:0 0 16px\">iTask</p>")
            .Append("<p style=\"font-size:15px;line-height:1.5\">").Append(Encode(intro)).Append("</p>");
        if (lines.Count > 0)
        {
            html.Append(list ? "<ul style=\"padding-left:18px;font-size:15px;line-height:1.6\">" : "<div style=\"font-size:15px\">");
            foreach (var line in lines)
            {
                var content = line.Link is null
                    ? Encode(line.Text)
                    : $"<a href=\"{Encode(line.Link)}\" style=\"color:#1D4ED8\">{Encode(line.Text)}</a>";
                html.Append(list ? $"<li>{content}</li>" : $"<p>{content}</p>");
            }

            html.Append(list ? "</ul>" : "</div>");
        }

        if (action is { } b)
        {
            html.Append("<p style=\"margin:24px 0\"><a href=\"").Append(Encode(b.Url))
                .Append("\" style=\"background:#1D5BF5;color:#fff;text-decoration:none;padding:12px 22px;border-radius:999px;font-weight:600;font-size:14px\">")
                .Append(Encode(b.Label)).Append("</a></p>");
        }

        html.Append("<p style=\"font-size:12px;color:#6B7280\">").Append(Encode(footer)).Append("</p></div>");
        return new EmailMessage(to, subject, text.ToString(), html.ToString());
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
