using ITask.Api.Domain;

namespace ITask.Api.Infrastructure.Demo;

/// <summary>
/// The seeded demo accounts, keyed by the role name used in <c>/api/auth/demo/{role}</c>.
/// Addresses use the reserved example.com domain.
/// </summary>
public static class DemoAccounts
{
    public sealed record Account(string Key, string Email, string Name, BoardRole? BoardRole, bool PlatformAdmin);

    public static IReadOnlyList<Account> All { get; } =
    [
        new("admin", "demo-admin@example.com", "Demo Admin", null, true),
        new("owner", "demo-owner@example.com", "Demo Owner", BoardRole.Owner, false),
        new("editor", "demo-editor@example.com", "Demo Editor", BoardRole.Editor, false),
        new("viewer", "demo-viewer@example.com", "Demo Viewer", BoardRole.Viewer, false),
    ];

    public static Account? Find(string key)
        => All.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
}
