using ITask.Api.Common.Files;
using ITask.Api.Common.Persistence;
using ITask.Api.Infrastructure.Demo;
using ITask.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ITask.Api.Infrastructure.Maintenance;

/// <summary>
/// Resets what the demo accounts changed: deletes boards whose members are all demo accounts,
/// removes demo accounts from other boards, then seeds the sample board again.
/// </summary>
internal sealed class DemoResetJob(AppDbContext db, DemoSeeder seeder, IBlobStorage storage, IOptions<DemoOptions> options) : IMaintenanceJob
{
    public string Name => "demo-reset";

    public async Task RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        if (!options.Value.IsActive)
        {
            return;
        }

        var demoUsers = db.Users.Where(u => u.IsDemo).Select(u => u.Id);

        var boardIds = await db.Boards
            .Where(b => b.Members.Any() && b.Members.All(m => m.User!.IsDemo))
            .Select(b => b.Id)
            .ToListAsync(ct);

        await db.Boards.Where(b => boardIds.Contains(b.Id)).ExecuteDeleteAsync(ct);
        foreach (var boardId in boardIds)
        {
            await storage.DeletePrefixAsync(AttachmentRules.BoardPrefix(boardId), ct);
        }

        await db.CardAssignees.Where(a => demoUsers.Contains(a.UserId)).ExecuteDeleteAsync(ct);
        await db.ChecklistItems.Where(i => i.AssigneeId != null && demoUsers.Contains(i.AssigneeId.Value))
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.AssigneeId, (Guid?)null), ct);
        await db.BoardStars.Where(s => demoUsers.Contains(s.UserId)).ExecuteDeleteAsync(ct);
        await db.BoardMembers.Where(m => demoUsers.Contains(m.UserId)).ExecuteDeleteAsync(ct);
        await db.BoardTemplates.Where(t => demoUsers.Contains(t.OwnerId)).ExecuteDeleteAsync(ct);

        await seeder.SeedAsync(ct);
    }
}
