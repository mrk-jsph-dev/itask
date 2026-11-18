namespace ITask.Api.Tests.Infrastructure;

/// <summary>An <see cref="ApiFactory"/> with demo mode on, so the demo accounts are seeded.</summary>
public sealed class DemoApiFactory : ApiFactory
{
    protected override Dictionary<string, string?> ExtraConfig => new()
    {
        ["Demo:Enabled"] = "true",
        ["Demo:Password"] = "Demo-password-123!",
    };
}
