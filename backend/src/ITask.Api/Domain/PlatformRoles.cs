namespace ITask.Api.Domain;

/// <summary>
/// ASP.NET Core Identity role names for app-wide permissions. These never grant board
/// access; that comes only from a <see cref="BoardMember"/> row.
/// </summary>
public static class PlatformRoles
{
    public const string PlatformAdmin = "PlatformAdmin";
}
