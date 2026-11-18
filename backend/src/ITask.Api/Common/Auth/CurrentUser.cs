using System.Security.Claims;

namespace ITask.Api.Common.Auth;

public interface ICurrentUser
{
    Guid Id { get; }

    bool IsAuthenticated { get; }
}

internal sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public Guid Id
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id)
                ? id
                : throw new InvalidOperationException("No authenticated user on this request.");
        }
    }
}
