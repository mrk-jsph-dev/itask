namespace ITask.Api.Common.Endpoints;

/// <summary>
/// Maps one feature slice's route(s). Implementations are discovered by
/// <see cref="EndpointExtensions.AddEndpoints"/> and mapped under <c>/api</c>.
/// </summary>
public interface IEndpoint
{
    void Map(IEndpointRouteBuilder app);
}
