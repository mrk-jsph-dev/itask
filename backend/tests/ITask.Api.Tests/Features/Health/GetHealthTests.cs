using System.Net.Http.Json;
using ITask.Api.Features.Health;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Health;

public sealed class GetHealthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Returns_ok()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.GetFromJsonAsync<GetHealth.Response>("/api/health");

        Assert.Equal("ok", response?.Status);
    }
}
