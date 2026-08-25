using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NOVAxis.Web.Auth;
using NOVAxis.Web.Contracts;

namespace NOVAxis.Web.Api
{
    public static class AuthEndpoints
    {
        public static IEndpointRouteBuilder MapAuthApi(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/auth");

            group.MapGet("/login", Login).AllowAnonymous();
            group.MapPost("/logout", Logout);
            group.MapGet("/me", Me).AllowAnonymous();

            return routes;
        }

        private static IResult Login(string returnUrl = null)
        {
            // Only paths inside the app may be returned to - anything else could
            // bounce a fresh login onto a foreign site
            var target = IsLocal(returnUrl) ? returnUrl : "/";

            var properties = new AuthenticationProperties { RedirectUri = target };

            return Results.Challenge(properties, new[] { DiscordAuthentication.Scheme });
        }

        private static async Task<IResult> Logout(ClaimsPrincipal user, HttpContext context)
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }

        private static IResult Me(ClaimsPrincipal user)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return Results.Unauthorized();

            var id = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var avatar = user.FindFirstValue(DiscordAuthentication.AvatarClaim);

            var avatarUrl = !string.IsNullOrEmpty(avatar)
                ? $"https://cdn.discordapp.com/avatars/{id}/{avatar}.png?size=128"
                : $"https://cdn.discordapp.com/embed/avatars/{(ulong.Parse(id) >> 22) % 6}.png";

            return Results.Ok(new WebUserDto(id, user.Identity.Name, avatarUrl));
        }

        private static bool IsLocal(string url)
        {
            return !string.IsNullOrEmpty(url)
                && url[0] == '/'
                && (url.Length == 1 || url[1] is not ('/' or '\\'));
        }
    }
}
