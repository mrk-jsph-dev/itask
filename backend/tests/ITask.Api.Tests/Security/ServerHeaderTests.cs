using ITask.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ITask.Api.Tests.Security;

public sealed class ServerHeaderTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void Kestrel_does_not_send_a_server_header() =>
        Assert.False(factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.AddServerHeader);
}
