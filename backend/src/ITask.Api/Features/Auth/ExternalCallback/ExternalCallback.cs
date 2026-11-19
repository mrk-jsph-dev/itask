using System.Security.Claims;
using ITask.Api.Common.Auth;
using ITask.Api.Common.Endpoints;
using ITask.Api.Common.Security;
using ITask.Api.Domain;
using ITask.Api.Infrastructure.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using ChallengeSlice = ITask.Api.Features.Auth.ExternalChallenge.ExternalChallenge;

namespace ITask.Api.Features.Auth.ExternalCallback;

/// <summary>
/// Lands after the provider handler has stored the external identity in Identity's external cookie.
/// Finds the user (by login, else by a verified email on a confirmed account, else creates one), or for
/// linking the ticket's user, then redirects to the SPA's <c>/auth/external</c> page with a one-time code
/// or an <c>error</c>. It never sets the auth cookie itself: see <see cref="ExternalSignInCodes"/>.
/// </summary>
public static class ExternalCallback
{
    public static class Errors
    {
        public const string Failed = "failed";
        public const string EmailUnverified = "email_unverified";
        public const string AccountExists = "account_exists";
        public const string LoginTaken = "login_taken";
    }

    internal sealed class Endpoint : IEndpoint
    {
        public void Map(IEndpointRouteBuilder app) =>
            app.MapGet("/auth/external/callback", Handle)
                .WithTags("Auth")
                .RequireRateLimiting(RateLimits.Auth);

        private static async Task<RedirectHttpResult> Handle(
            HttpContext http,
            UserManager<AppUser> users,
            PlatformAdmins admins,
            ExternalSignInCodes codes,
            IOptions<EmailOptions> email)
        {
            var result = await http.AuthenticateAsync(IdentityConstants.ExternalScheme);
            await http.SignOutAsync(IdentityConstants.ExternalScheme);

            string? Item(string name) => result.Properties?.Items.TryGetValue(name, out var value) == true ? value : null;

            var provider = ExternalLogins.FindByScheme(Item("LoginProvider"));
            var key = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var nonce = Item(ChallengeSlice.NonceItem);
            if (!result.Succeeded || provider is null || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(nonce))
            {
                return Back(email.Value, error: Errors.Failed);
            }

            var principal = result.Principal!;
            var accountName = principal.FindFirstValue(ExternalLogins.AccountNameClaim);
            var login = new UserLoginInfo(provider.Scheme, key, accountName);
            var owner = await users.FindByLoginAsync(provider.Scheme, key);

            if (Guid.TryParseExact(Item(ChallengeSlice.LinkUserItem), "N", out var linkUserId))
            {
                if (owner is not null && owner.Id != linkUserId)
                {
                    return Back(email.Value, error: Errors.LoginTaken);
                }

                var target = await users.FindByIdAsync(linkUserId.ToString());
                var linkCode = target is null
                    ? null
                    : await codes.IssueAsync(target, nonce, new PendingLogin(login.LoginProvider, login.ProviderKey, accountName));
                return Back(email.Value, code: linkCode, error: linkCode is null ? Errors.Failed : null);
            }

            if (owner is null)
            {
                var (user, error) = await FindOrCreateByEmailAsync(principal, login, users, admins);
                if (user is null)
                {
                    return Back(email.Value, error: error);
                }

                owner = user;
            }

            var code = await codes.IssueAsync(owner, nonce, link: null);
            return Back(email.Value, code: code, error: code is null ? Errors.Failed : null);
        }

        private static async Task<(AppUser? User, string? Error)> FindOrCreateByEmailAsync(
            ClaimsPrincipal principal, UserLoginInfo login, UserManager<AppUser> users, PlatformAdmins admins)
        {
            if (principal.FindFirstValue(ExternalLogins.VerifiedEmailClaim) is not { Length: > 0 } address)
            {
                return (null, Errors.EmailUnverified);
            }

            var existing = await users.FindByEmailAsync(address);
            if (existing is not null)
            {
                // Linking to an unconfirmed account would hand it to whoever registered that email first.
                if (!existing.EmailConfirmed || existing.IsDemo)
                {
                    return (null, Errors.AccountExists);
                }

                return (await users.AddLoginAsync(existing, login)).Succeeded ? (existing, null) : (null, Errors.Failed);
            }

            var user = new AppUser
            {
                UserName = address,
                Email = address,
                EmailConfirmed = true,
                DisplayName = DisplayName(principal, address),
            };
            if (!(await users.CreateAsync(user)).Succeeded || !(await users.AddLoginAsync(user, login)).Succeeded)
            {
                return (null, Errors.Failed);
            }

            await admins.GrantIfListedAsync(user);
            return (user, null);
        }

        private static string DisplayName(ClaimsPrincipal principal, string address)
        {
            var name = principal.FindFirstValue("urn:github:name") ?? principal.FindFirstValue(ClaimTypes.Name);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = address.Split('@')[0];
            }

            name = name.Trim();
            return name.Length > 100 ? name[..100] : name;
        }

        private static RedirectHttpResult Back(EmailOptions email, string? code = null, string? error = null)
        {
            var page = email.Link("auth/external") ?? "/auth/external";
            var query = error is null
                ? new Dictionary<string, string?> { ["code"] = code }
                : new Dictionary<string, string?> { ["error"] = error };
            return TypedResults.Redirect(QueryHelpers.AddQueryString(page, query));
        }
    }
}
