using System.Net;
using ITask.Api.Infrastructure.Turnstile;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ITask.Api.Tests.Infrastructure;

/// <summary>An <see cref="ApiFactory"/> whose Turnstile verifier rejects every token.</summary>
public sealed class RejectingTurnstileApiFactory : ApiFactory
{
    protected override void ConfigureServices(IServiceCollection services)
        => services.Replace(ServiceDescriptor.Singleton<ITurnstileVerifier, RejectingVerifier>());

    private sealed class RejectingVerifier : ITurnstileVerifier
    {
        public Task<bool> VerifyAsync(string? token, IPAddress? remoteIp, CancellationToken ct) => Task.FromResult(false);
    }
}
