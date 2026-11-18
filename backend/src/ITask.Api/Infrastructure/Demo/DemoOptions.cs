namespace ITask.Api.Infrastructure.Demo;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    /// <summary>Enables <see cref="DemoSeeder"/> at startup.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Password for all demo accounts. Read from user secrets or Key Vault; seeding is
    /// skipped when it is empty.
    /// </summary>
    public string? Password { get; set; }
}
