namespace ITask.Api.Tests.Infrastructure;

/// <summary>An <see cref="ApiFactory"/> whose <c>Admin:Emails</c> lists <see cref="AdminEmail"/>.</summary>
public sealed class AdminEmailsApiFactory : ApiFactory
{
    public const string AdminEmail = "owner-admin@example.com";

    protected override Dictionary<string, string?> ExtraConfig => new()
    {
        ["Admin:Emails"] = $" {AdminEmail.ToUpperInvariant()} ; other@example.com ",
    };
}
