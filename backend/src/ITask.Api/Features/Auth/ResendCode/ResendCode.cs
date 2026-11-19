using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Email;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Auth.ResendCode;

/// <summary>
/// Emails a new sign-up code to an unconfirmed account, within <see cref="EmailCodes"/>' cooldown and
/// daily limit. Always answers 200 so it can't be used to find out which emails have accounts.
/// Also takes over Identity's <c>resendConfirmationEmail</c> route so its link email can't be sent.
/// </summary>
public static class ResendCode
{
    public sealed record Request(string Email);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator() => RuleFor(r => r.Email).NotEmpty().MaximumLength(256);
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app)
        {
            foreach (var route in new[] { "/auth/resend-code", "/auth/resendConfirmationEmail" })
            {
                app.MapPost(route, Handle)
                    .WithTags("Auth")
                    .WithOrder(-1)
                    .RequireRateLimiting(RateLimits.Auth)
                    .RequireTurnstile()
                    .WithValidation<Request>();
            }
        }

        private static async Task<Results<Ok, ValidationProblem>> Handle(
            Request request, UserManager<AppUser> users, EmailCodes codes, IAppEmailSender emails, CancellationToken ct)
        {
            var user = await users.FindByEmailAsync(request.Email.Trim());
            if (user is { EmailConfirmed: false, IsDemo: false, Email: { } email }
                && await codes.IssueAsync(user) is { } code)
            {
                await emails.SendAsync(EmailTemplates.SignUpCode(email, code), ct);
            }

            return TypedResults.Ok();
        }
    }
}
