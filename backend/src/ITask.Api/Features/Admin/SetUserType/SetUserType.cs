using System.Text.Json.Serialization;
using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Admin.SetUserType;

/// <summary>
/// Makes an account a Platform admin or a regular User. Platform admins manage accounts only;
/// board access still comes from board membership. Admins can't change their own type (so at
/// least one admin always remains), and demo accounts keep their seeded type. The change reaches
/// the user's cookie at the next security stamp check (within a minute).
/// </summary>
public static class SetUserType
{
    [JsonConverter(typeof(JsonStringEnumConverter<UserType>))]
    public enum UserType
    {
        User,
        PlatformAdmin,
    }

    public sealed record Request(UserType Type);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.Type).IsInEnum();
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPut("/admin/users/{userId:guid}/type", Handle)
                .WithTags("Admin")
                .RequireAuthorization(PlatformRoles.PlatformAdmin)
                .RequireNonDemo()
                .WithValidation<Request>();

        private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> Handle(
            Guid userId,
            Request request,
            UserManager<AppUser> users,
            PlatformAdmins admins,
            ICurrentUser currentUser)
        {
            if (userId == currentUser.Id)
            {
                return TypedResults.Problem(title: "You can't change your own user type.", statusCode: StatusCodes.Status409Conflict);
            }

            var user = await users.FindByIdAsync(userId.ToString());
            if (user is null)
            {
                return TypedResults.NotFound();
            }

            if (user.IsDemo)
            {
                return TypedResults.Problem(title: "Demo accounts keep their user type.", statusCode: StatusCodes.Status409Conflict);
            }

            var isAdmin = await users.IsInRoleAsync(user, PlatformRoles.PlatformAdmin);
            var makeAdmin = request.Type == UserType.PlatformAdmin;
            if (isAdmin == makeAdmin)
            {
                return TypedResults.NoContent();
            }

            await admins.SetAsync(user, makeAdmin, currentUser.Id);
            return TypedResults.NoContent();
        }
    }
}
