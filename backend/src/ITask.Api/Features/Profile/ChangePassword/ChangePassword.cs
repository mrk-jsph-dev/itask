using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Profile.ChangePassword;

/// <summary>Changes the caller's password and reissues the auth cookie. Not for demo accounts.</summary>
public static class ChangePassword
{
    public sealed record Request(string CurrentPassword, string NewPassword);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.CurrentPassword).NotEmpty();
            RuleFor(r => r.NewPassword).NotEmpty();
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/me/password", Handle)
                .WithTags("Profile")
                .RequireAuthorization()
                .RequireNonDemo()
                .WithValidation<Request>();

        private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> Handle(
            Request request, ICurrentUser currentUser, UserManager<AppUser> users, SignInManager<AppUser> signIn)
        {
            var user = await users.FindByIdAsync(currentUser.Id.ToString());
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded)
            {
                return TypedResults.ValidationProblem(result.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
            }

            await signIn.RefreshSignInAsync(user);
            return TypedResults.NoContent();
        }
    }
}
