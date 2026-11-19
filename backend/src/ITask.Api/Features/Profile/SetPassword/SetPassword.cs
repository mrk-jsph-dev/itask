using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Profile.SetPassword;

/// <summary>Adds a password to an account created through GitHub or Microsoft (409 when it has one).</summary>
public static class SetPassword
{
    public sealed record Request(string NewPassword);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.NewPassword).NotEmpty();
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/me/password/set", Handle)
                .WithTags("Profile")
                .RequireAuthorization()
                .RequireNonDemo()
                .WithValidation<Request>();

        private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> Handle(
            Request request, ICurrentUser currentUser, UserManager<AppUser> users, SignInManager<AppUser> signIn)
        {
            var user = await users.FindByIdAsync(currentUser.Id.ToString());
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            if (user.PasswordHash is not null)
            {
                return TypedResults.Problem(
                    title: "This account already has a password. Change it instead.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var result = await users.AddPasswordAsync(user, request.NewPassword);
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
