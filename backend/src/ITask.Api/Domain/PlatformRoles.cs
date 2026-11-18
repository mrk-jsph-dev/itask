namespace ITask.Api.Domain;

/// <summary>
/// App-level roles (v1). A Platform admin manages accounts and settings but does
/// not get access to other people's boards: board access always comes from
/// <see cref="BoardMember"/>.
/// </summary>
public static class PlatformRoles
{
    public const string PlatformAdmin = "PlatformAdmin";
}
