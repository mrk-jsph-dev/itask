using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Domain;

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Marks seeded demo accounts so features can restrict them (e.g. AI, uploads).</summary>
    public bool IsDemo { get; set; }
}
