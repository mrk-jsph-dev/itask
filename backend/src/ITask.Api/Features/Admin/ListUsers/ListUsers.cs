using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Admin.ListUsers;

/// <summary>
/// Pages through all accounts for Platform admins. Account data only, never board content.
/// <c>cursor</c> is the last id of the previous page.
/// </summary>
public static class ListUsers
{
    public sealed record Item(
        Guid Id, string Email, string DisplayName, bool IsPlatformAdmin, bool IsDemo, bool IsLocked, DateTimeOffset CreatedAt);

    public sealed record Response(List<Item> Items, Guid? NextCursor);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/admin/users", Handle)
                .WithTags("Admin")
                .RequireAuthorization(PlatformRoles.PlatformAdmin);

        private static async Task<Ok<Response>> Handle(
            AppDbContext db, TimeProvider clock, CancellationToken ct, Guid? cursor = null, int limit = 50)
        {
            limit = Math.Clamp(limit, 1, 100);

            var adminRoleId = await db.Roles
                .Where(r => r.Name == PlatformRoles.PlatformAdmin)
                .Select(r => (Guid?)r.Id)
                .FirstOrDefaultAsync(ct);

            var query = db.Users.AsNoTracking();
            if (cursor is { } after)
            {
                query = query.Where(u => u.Id.CompareTo(after) > 0);
            }

            var rows = await query
                .OrderBy(u => u.Id)
                .Take(limit + 1)
                .Select(u => new
                {
                    u.Id,
                    u.Email,
                    u.DisplayName,
                    IsAdmin = db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == adminRoleId),
                    u.IsDemo,
                    u.LockoutEnd,
                    u.CreatedAt,
                })
                .ToListAsync(ct);

            var now = clock.GetUtcNow();
            var items = rows.Take(limit)
                .Select(u => new Item(
                    u.Id, u.Email ?? string.Empty, u.DisplayName, u.IsAdmin, u.IsDemo, u.LockoutEnd > now, u.CreatedAt))
                .ToList();

            return TypedResults.Ok(new Response(items, rows.Count > limit ? items[^1].Id : null));
        }
    }
}
