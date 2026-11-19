using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace ITask.Api.Features.Profile.StartLink;

/// <summary>
/// Returns a short-lived ticket naming the caller, passed to <c>/api/auth/external/{provider}?link=</c>.
/// The external flow runs as a top-level page on the API's site, where the SPA's cookie may not be sent.
/// </summary>
public static class StartLink
{
    public sealed record Response(string Ticket);

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapPost("/me/logins/{provider}/link", Handle)
                .WithTags("Profile")
                .RequireAuthorization()
                .RequireNonDemo();

        private static async Task<Results<Ok<Response>, NotFound, ProblemHttpResult, UnauthorizedHttpResult>> Handle(
            string provider,
            ICurrentUser currentUser,
            UserManager<AppUser> users,
            IAuthenticationSchemeProvider schemes,
            ExternalSignInCodes codes)
        {
            var match = ExternalLogins.FindById(provider);
            if (match is null || await schemes.GetSchemeAsync(match.Scheme) is null)
            {
                return TypedResults.NotFound();
            }

            var user = await users.FindByIdAsync(currentUser.Id.ToString());
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            if ((await users.GetLoginsAsync(user)).Any(l => l.LoginProvider == match.Scheme))
            {
                return TypedResults.Problem(
                    title: "This provider is already connected.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            return TypedResults.Ok(new Response(codes.CreateLinkTicket(user.Id)));
        }
    }
}
