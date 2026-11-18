using System.Net;
using ITask.Api.Tests.Infrastructure;

namespace ITask.Api.Tests.Features.Health;

public sealed class GetReadinessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Healthy_when_the_database_answers()
    {
        var client = factory.CreateHttpsClient();

        var response = await client.GetAsync(new Uri("/api/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
