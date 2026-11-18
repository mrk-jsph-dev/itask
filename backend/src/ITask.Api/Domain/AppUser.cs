using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Domain;

public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Hex colour (#RRGGBB) for the initials avatar.</summary>
    public string AvatarColor { get; set; } = "#1D5BF5";

    /// <summary>IANA time zone id used to show dates in the user's local time.</summary>
    public string TimeZone { get; set; } = "UTC";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Marks seeded demo accounts so features can restrict them (e.g. AI, uploads).</summary>
    public bool IsDemo { get; set; }
}
