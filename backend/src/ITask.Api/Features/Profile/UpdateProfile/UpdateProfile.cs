using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Persistence;
using ITask.Api.Common.Validation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace ITask.Api.Features.Profile.UpdateProfile;

/// <summary>Updates the caller's display name, time zone and avatar colour.</summary>
public static partial class UpdateProfile
{
    public sealed record Request(string DisplayName, string TimeZone, string AvatarColor);

    public sealed record Response(string DisplayName, string TimeZone, string AvatarColor);

    public sealed partial class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(100);
            RuleFor(r => r.TimeZone)
                .NotEmpty()
                .MaximumLength(64)
                .Must(id => TimeZoneInfo.TryFindSystemTimeZoneById(id, out _))
                .WithMessage("Unknown time zone.");
            RuleFor(r => r.AvatarColor).NotEmpty().Matches(HexColor()).WithMessage("Use a #RRGGBB colour.");
        }

        [System.Text.RegularExpressions.GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
        private static partial System.Text.RegularExpressions.Regex HexColor();
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/me", Handle)
                .WithTags("Profile")
                .RequireAuthorization()
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, UnauthorizedHttpResult>> Handle(
            Request request, ICurrentUser currentUser, AppDbContext db, CancellationToken ct)
        {
            var user = await db.Users.FindAsync([currentUser.Id], ct);
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            user.DisplayName = request.DisplayName.Trim();
            user.TimeZone = request.TimeZone;
            user.AvatarColor = request.AvatarColor.ToUpperInvariant();
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(new Response(user.DisplayName, user.TimeZone, user.AvatarColor));
        }
    }
}
