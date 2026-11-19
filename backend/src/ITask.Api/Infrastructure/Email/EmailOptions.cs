namespace ITask.Api.Infrastructure.Email;

public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>Sender name shown to recipients.</summary>
    public string FromName { get; set; } = "iTask";

    /// <summary>Sender address; defaults to <see cref="SmtpOptions.Username"/> (Gmail requires that).</summary>
    public string FromAddress { get; set; } = "";

    /// <summary>Site origin used in links, e.g. <c>https://app.example.com</c>. Empty = no links.</summary>
    public string PublicUrl { get; set; } = "";

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Most emails the app sends per UTC day (Gmail allows about 500).</summary>
    public int DailyLimit { get; set; } = 400;

    /// <summary>Most emails one address receives per UTC day, so no inbox can be flooded through iTask.</summary>
    public int DailyPerRecipient { get; set; } = 10;

    /// <summary>Absolute site URL for <paramref name="path"/>, or null without <see cref="PublicUrl"/>.</summary>
    public string? Link(string path) =>
        string.IsNullOrWhiteSpace(PublicUrl) ? null : $"{PublicUrl.TrimEnd('/')}/{path.TrimStart('/')}";
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = "smtp.gmail.com";

    /// <summary>587 uses STARTTLS; 465 uses TLS from the start.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Empty = emails are logged, not sent.</summary>
    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
}
