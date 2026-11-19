using FluentValidation;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Common.Validation;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Email;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Auth.Register;

/// <summary>
/// Creates an account with a display name and emails a six-digit code to confirm the address
/// (see <c>VerifyEmail</c>). Replaces Identity's own register endpoint at the same route (lower
/// <c>Order</c> wins the match). Sign-ups across the whole app are capped per day
/// (<c>Auth:DailySignups</c>) so scripted registrations can't pile up.
/// </summary>
public static class Register
{
    public sealed record Request(string DisplayName, string Email, string Password);

    /// <summary>The address the code was sent to.</summary>
    public sealed record Response(string Email);

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

        private static async Task<Results<Ok<Response>, ValidationProblem, ProblemHttpResult>> Handle(
            Request request,
            UserManager<AppUser> users,
            PlatformAdmins admins,
            EmailCodes codes,
            DailyCounters counters,
            IAppEmailSender emails,
            IConfiguration config,
            CancellationToken ct)
        {
            if (!await counters.TryIncrementAsync(DailyCounters.Signups, config.GetValue("Auth:DailySignups", 50), ct))
            {
                return TypedResults.Problem(
                    title: "Too many sign-ups today. Please try again tomorrow.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

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

            if (await codes.IssueAsync(user) is { } code)
            {
                await emails.SendAsync(EmailTemplates.SignUpCode(email, code), ct);
            }

            return TypedResults.Ok(new Response(email));
        }
    }
}
