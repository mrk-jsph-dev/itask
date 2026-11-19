using System.Collections.Concurrent;
using ITask.Api.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>Keeps sent emails in memory instead of logging them.</summary>
public sealed class CapturingEmailSender : IAppEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public bool CanSend => true;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address) =>
        [.. Sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase))];

    public IReadOnlyList<EmailMessage> Digests(string address) =>
        [.. To(address).Where(m => m.Subject.Contains("digest", StringComparison.Ordinal))];
}

/// <summary>An <see cref="ApiFactory"/> whose emails land in <see cref="Emails"/>, with a site URL for links.</summary>
public sealed class EmailApiFactory : ApiFactory
{
    public CapturingEmailSender Emails { get; } = new();

    protected override Dictionary<string, string?> ExtraConfig => new()
    {
        ["Email:PublicUrl"] = "https://app.example.test",
    };

    protected override void ConfigureServices(IServiceCollection services) =>
        services.Replace(ServiceDescriptor.Singleton<IAppEmailSender>(Emails));
}
