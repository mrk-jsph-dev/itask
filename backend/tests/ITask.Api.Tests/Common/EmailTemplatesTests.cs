using ITask.Api.Infrastructure.Email;

namespace ITask.Api.Tests.Common;

public sealed class EmailTemplatesTests
{
    [Fact]
    public void User_text_is_encoded_in_the_html_part()
    {
        var mail = EmailTemplates.Invite("a@example.com", "<b>Eve</b>", "Board & \"co\"", "Editor", "https://x.test/invite/t");

        Assert.Contains("&lt;b&gt;Eve&lt;/b&gt;", mail.Html, StringComparison.Ordinal);
        Assert.Contains("Board &amp; &quot;co&quot;", mail.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>Eve</b>", mail.Html, StringComparison.Ordinal);
        Assert.Contains("<b>Eve</b> invited you", mail.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Password_reset_without_a_site_url_still_carries_the_code()
    {
        var mail = EmailTemplates.PasswordReset("a@example.com", null, "CODE123");

        Assert.Contains("CODE123", mail.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Choose a new password", mail.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Digest_lists_each_line_with_its_link()
    {
        var mail = EmailTemplates.Digest("a@example.com", "Dana", [new DigestLine("Ann mentioned you", "https://x.test/c")], "daily", null);

        Assert.Equal("Your iTask daily digest: 1 new", mail.Subject);
        Assert.Contains("- Ann mentioned you", mail.Text, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://x.test/c\"", mail.Html, StringComparison.Ordinal);
    }
}
