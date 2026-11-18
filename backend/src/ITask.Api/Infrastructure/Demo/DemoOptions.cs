namespace ITask.Api.Infrastructure.Demo;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    /// <summary>Only true in the demo environment. Never seed demo users elsewhere.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Shared demo password. Comes from user secrets locally and Key Vault in Azure;
    /// it is never committed. Seeding is skipped when it is missing.
    /// </summary>
    public string? Password { get; set; }
}
