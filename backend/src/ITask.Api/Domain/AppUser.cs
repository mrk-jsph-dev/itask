using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Domain;

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Demo accounts are read-mostly: no AI, integrations or uploads.</summary>
    public bool IsDemo { get; set; }
}
