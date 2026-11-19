using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Templates.DeleteTemplate;

/// <summary>Deletes one of the caller's saved templates. Built-in and other users' templates are 404.</summary>
public static class DeleteTemplate
{
    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapDelete("/templates/{templateId:guid}", Handle)
                .WithTags("Templates")
                .RequireAuthorization();

        private static async Task<Results<NoContent, NotFound>> Handle(
            Guid templateId, AppDbContext db, ICurrentUser user, CancellationToken ct)
        {
            var deleted = await db.BoardTemplates
                .Where(t => t.Id == templateId && t.OwnerId == user.Id)
                .ExecuteDeleteAsync(ct);
            return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
        }
    }
}
