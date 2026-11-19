using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Auth.CompleteExternal;

/// <summary>
/// Redeems the one-time code from the external callback with the nonce the browser kept, so a code
/// pasted into another browser is useless. A sign-in code sets the auth cookie; a link code adds the
/// login to the caller, who must be signed in as the user that started linking.
/// </summary>
public static class CompleteExternal
{
    public sealed record Request(string Code, string Nonce);

    public sealed record Response(string Mode);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Code).NotEmpty().MaximumLength(200);
            RuleFor(r => r.Nonce).NotEmpty().MaximumLength(128);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/auth/external/complete", Handle)
                .WithTags("Auth")
                .RequireRateLimiting(RateLimits.Auth)
                .WithValidation<Request>();

        private static async Task<Results<Ok<Response>, ProblemHttpResult, ValidationProblem>> Handle(
            Request request,
            ExternalSignInCodes codes,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn,
            ICurrentUser currentUser)
        {
            if (await codes.RedeemAsync(request.Code, request.Nonce) is not { } redeemed)
            {
                return TypedResults.Problem(
                    title: "This sign-in has expired. Please try again.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var user = redeemed.User;
            if (await users.IsLockedOutAsync(user) || !await signIn.CanSignInAsync(user))
            {
                return Forbidden("This account can't sign in right now.");
            }

            if (redeemed.Link is not { } link)
            {
                await signIn.SignInAsync(user, isPersistent: true, authenticationMethod: "external");
                return TypedResults.Ok(new Response("signIn"));
            }

            if (!currentUser.IsAuthenticated || currentUser.Id != user.Id || user.IsDemo)
            {
                return Forbidden("Sign in to the account you're connecting, then try again.");
            }

            var logins = await users.GetLoginsAsync(user);
            if (!logins.Any(l => l.LoginProvider == link.Scheme && l.ProviderKey == link.Key))
            {
                if (logins.Any(l => l.LoginProvider == link.Scheme))
                {
                    return TypedResults.Problem(
                        title: "Another account from this provider is already connected.",
                        statusCode: StatusCodes.Status409Conflict);
                }

                var added = await users.AddLoginAsync(user, new UserLoginInfo(link.Scheme, link.Key, link.DisplayName));
                if (!added.Succeeded)
                {
                    return TypedResults.Problem(
                        title: "This account is already connected to another iTask user.",
                        statusCode: StatusCodes.Status409Conflict);
                }
            }

            return TypedResults.Ok(new Response("link"));
        }

        private static ProblemHttpResult Forbidden(string title) =>
            TypedResults.Problem(title: title, statusCode: StatusCodes.Status403Forbidden);
    }
}
