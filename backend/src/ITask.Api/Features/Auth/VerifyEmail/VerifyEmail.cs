using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Auth.VerifyEmail;

/// <summary>
/// Confirms a new account's email with the six-digit code from <c>Register</c> and signs the user in.
/// Unknown emails, confirmed accounts and wrong or expired codes all get the same answer.
/// </summary>
public static class VerifyEmail
{
    public sealed record Request(string Email, string Code);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Email).NotEmpty().MaximumLength(256);
            RuleFor(r => r.Code).NotEmpty().Matches($"^[0-9]{{{EmailCodes.Length}}}$")
                .WithMessage($"Enter the {EmailCodes.Length}-digit code.");
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/auth/verify-email", Handle)
                .WithTags("Auth")
                .RequireRateLimiting(RateLimits.Auth)
                .WithValidation<Request>();

        private static async Task<Results<Ok, ValidationProblem, ProblemHttpResult>> Handle(
            Request request, UserManager<AppUser> users, SignInManager<AppUser> signIn, EmailCodes codes)
        {
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is null || user.EmailConfirmed || user.IsDemo || !await codes.VerifyAsync(user, request.Code))
            {
                return TypedResults.Problem(
                    title: "That code is wrong or has expired.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            user.EmailConfirmed = true;
            await users.UpdateAsync(user);

            if (await users.IsLockedOutAsync(user))
            {
                return TypedResults.Problem(title: "This account is locked.", statusCode: StatusCodes.Status403Forbidden);
            }

            await signIn.SignInAsync(user, isPersistent: true);
            return TypedResults.Ok();
        }
    }
}
