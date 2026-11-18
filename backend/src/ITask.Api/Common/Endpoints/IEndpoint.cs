namespace ITask.Api.Common.Endpoints;

/// <summary>
/// One use case = one endpoint. Every slice under Features/ implements this
/// and is discovered automatically at startup.
/// </summary>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder app);
}
