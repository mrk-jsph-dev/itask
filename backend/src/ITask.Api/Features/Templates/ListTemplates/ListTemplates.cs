using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Templates;
using ITask.Api.Infrastructure.Templates;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ITask.Api.Features.Templates.ListTemplates;

/// <summary>The built-in templates and the caller's saved ones, newest first.</summary>
public static class ListTemplates
{
    public sealed record Response(IReadOnlyList<TemplateSummary> BuiltIn, IReadOnlyList<TemplateSummary> Mine);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/templates", Handle)
                .WithTags("Templates")
                .RequireAuthorization();

        private static async Task<Ok<Response>> Handle(AppDbContext db, ICurrentUser user, ITemplateCatalog catalog, CancellationToken ct)
        {
            var mine = await db.BoardTemplates
                .AsNoTracking()
                .Where(t => t.OwnerId == user.Id)
                .OrderByDescending(t => t.Id)
                .ToListAsync(ct);

            return TypedResults.Ok(new Response(
                [.. catalog.All.Select(t => t.Summary())],
                [.. mine.Select(TemplateSummary.Of)]));
        }
    }
}
