using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Profile.UpdatePreferences;

/// <summary>Changes the caller's display preferences; fields left out keep their value.</summary>
public static class UpdatePreferences
{
    public sealed record Request(ThemePreference? ThemePreference);

    public sealed record Response(ThemePreference ThemePreference);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.ThemePreference).IsInEnum();
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPatch("/me", Handle)
                .WithTags("Profile")
                .RequireAuthorization()
                .RequireNonDemo()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, UnauthorizedHttpResult>> Handle(
            Request request, ICurrentUser currentUser, AppDbContext db, CancellationToken ct)
        {
            var user = await db.Users.FindAsync([currentUser.Id], ct);
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            if (request.ThemePreference is { } theme)
            {
                user.ThemePreference = theme;
                await db.SaveChangesAsync(ct);
            }

            return TypedResults.Ok(new Response(user.ThemePreference));
        }
    }
}
