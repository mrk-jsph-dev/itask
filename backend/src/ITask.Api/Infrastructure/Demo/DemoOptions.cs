namespace ITask.Api.Infrastructure.Demo;

public sealed class DemoOptions
{
    public const string Section = "Demo";

    /// <summary>Enables <see cref="DemoSeeder"/> at startup.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Password for all demo accounts. Read from user secrets or a Container Apps secret; seeding is
    /// skipped when it is empty.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>True when demo accounts exist and the demo sign-in buttons should be offered.</summary>
    public bool IsActive => Enabled && !string.IsNullOrWhiteSpace(Password);
}
