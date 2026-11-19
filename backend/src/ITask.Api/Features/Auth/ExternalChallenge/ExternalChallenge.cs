using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ITask.Api.Features.Auth.ExternalChallenge;

/// <summary>
/// Starts an external sign-in (a top-level browser navigation). The browser's nonce, and for linking the
/// ticket's user id, travel inside the provider's protected state to the callback.
/// </summary>
public static class ExternalChallenge
{
    public const string NonceItem = "itask.nonce";
    public const string LinkUserItem = "itask.linkUser";

    public sealed record Request(string Nonce, string? Link);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.Nonce).NotEmpty().MinimumLength(16).MaximumLength(128);
            RuleFor(r => r.Link).MaximumLength(1000);
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/auth/external/{provider}", Handle)
                .WithTags("Auth")
                .RequireRateLimiting(RateLimits.Auth)
                .WithValidation<Request>();

        private static async Task<Results<ChallengeHttpResult, NotFound, ProblemHttpResult>> Handle(
            string provider,
            [AsParameters] Request request,
            IAuthenticationSchemeProvider schemes,
            ExternalSignInCodes codes)
        {
            var match = ExternalLogins.FindById(provider);
            if (match is null || await schemes.GetSchemeAsync(match.Scheme) is null)
            {
                return TypedResults.NotFound();
            }

            var properties = new AuthenticationProperties { RedirectUri = "/api/auth/external/callback" };
            properties.Items["LoginProvider"] = match.Scheme;
            properties.Items[NonceItem] = request.Nonce;

            if (!string.IsNullOrEmpty(request.Link))
            {
                if (codes.ReadLinkTicket(request.Link) is not { } userId)
                {
                    return TypedResults.Problem(
                        title: "This link request has expired. Try again from your profile.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                properties.Items[LinkUserItem] = userId.ToString("N");
            }

            return TypedResults.Challenge(properties, [match.Scheme]);
        }
    }
}
