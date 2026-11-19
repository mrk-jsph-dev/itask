using System.Text;
using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace ITask.Api.Features.Auth.Register;

/// <summary>
/// Creates an account with a display name. Replaces Identity's own register endpoint at the
/// same route (lower <c>Order</c> wins the match) and sends the same confirmation email.
/// </summary>
public static class Register
{
    public sealed record Request(string DisplayName, string Email, string Password);

    public sealed class Validator : AbstractValidator<Request>
    {
        public Validator()
        {
            RuleFor(r => r.DisplayName).NotEmpty().MaximumLength(100);
            RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
            RuleFor(r => r.Password).NotEmpty();
        }
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/auth/register", Handle)
                .WithTags("Auth")
                .WithOrder(-1)
                .RequireRateLimiting(RateLimits.Auth)
                .RequireTurnstile()
                .WithValidation<Request>();

        private static async Task<Results<Ok, ValidationProblem>> Handle(
            Request request,
            UserManager<AppUser> users,
            PlatformAdmins admins,
            IEmailSender<AppUser> emailSender,
            HttpContext http)
        {
            var email = request.Email.Trim();
            var user = new AppUser { UserName = email, Email = email, DisplayName = request.DisplayName.Trim() };

            var result = await users.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return TypedResults.ValidationProblem(result.Errors
                    .GroupBy(e => e.Code)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
            }

            await admins.GrantIfListedAsync(user);

            var code = await users.GenerateEmailConfirmationTokenAsync(user);
            var query = new Dictionary<string, string?>
            {
                ["userId"] = user.Id.ToString(),
                ["code"] = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code)),
            };
            var link = QueryHelpers.AddQueryString(
                $"{http.Request.Scheme}://{http.Request.Host}/api/auth/confirmEmail", query);
            await emailSender.SendConfirmationLinkAsync(user, email, link);

            return TypedResults.Ok();
        }
    }
}
