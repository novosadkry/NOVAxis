using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Core;

namespace NOVAxis.Web.Auth
{
    /// <summary>
    /// Signs users in through Discord's OAuth2 and keeps them in a cookie. Only the
    /// "identify" scope is requested - guild membership is the bot's own knowledge,
    /// so no user token is worth storing.
    /// </summary>
    public static class DiscordAuthentication
    {
        public const string Scheme = "Discord";
        public const string AvatarClaim = "urn:discord:avatar";

        public static IServiceCollection AddDiscordAuthentication(this IServiceCollection collection, WebOptions options)
        {
            var secure = options.PublicUrl?.StartsWith("https", StringComparison.OrdinalIgnoreCase) == true;

            collection
                .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(cookie =>
                {
                    cookie.Cookie.Name = "novaxis.session";
                    cookie.Cookie.HttpOnly = true;
                    cookie.Cookie.SameSite = SameSiteMode.Lax;
                    cookie.Cookie.SecurePolicy = secure
                        ? CookieSecurePolicy.Always
                        : CookieSecurePolicy.SameAsRequest;

                    cookie.SlidingExpiration = true;
                    cookie.ExpireTimeSpan = TimeSpan.FromDays(14);

                    // An api serves status codes, not login pages
                    cookie.Events.OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    };

                    cookie.Events.OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    };
                })
                .AddOAuth(Scheme, oauth =>
                {
                    oauth.ClientId = options.OAuth.ClientId;
                    oauth.ClientSecret = options.OAuth.ClientSecret;

                    oauth.AuthorizationEndpoint = "https://discord.com/oauth2/authorize";
                    oauth.TokenEndpoint = "https://discord.com/api/oauth2/token";
                    oauth.UserInformationEndpoint = "https://discord.com/api/users/@me";
                    oauth.CallbackPath = "/api/auth/callback";

                    oauth.Scope.Add("identify");
                    oauth.SaveTokens = false;
                    oauth.UsePkce = true;

                    // The callback is a top-level redirect, and None would need https
                    oauth.CorrelationCookie.SameSite = SameSiteMode.Lax;
                    oauth.CorrelationCookie.HttpOnly = true;

                    oauth.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "id");
                    oauth.ClaimActions.MapJsonKey(AvatarClaim, "avatar");
                    oauth.ClaimActions.MapCustomJson(ClaimTypes.Name, user =>
                        user.TryGetProperty("global_name", out var name) && name.ValueKind == JsonValueKind.String
                            ? name.GetString()
                            : user.GetProperty("username").GetString());

                    oauth.Events.OnCreatingTicket = async context =>
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, context.Options.UserInformationEndpoint);
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

                        using var response = await context.Backchannel.SendAsync(
                            request, HttpCompletionOption.ResponseHeadersRead, context.HttpContext.RequestAborted);

                        response.EnsureSuccessStatusCode();

                        using var user = JsonDocument.Parse(
                            await response.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));

                        context.RunClaimActions(user.RootElement);
                    };
                });

            return collection;
        }

        public static ulong GetDiscordId(this ClaimsPrincipal principal)
        {
            var id = principal?.FindFirstValue(ClaimTypes.NameIdentifier);

            return ulong.TryParse(id, out var parsed) ? parsed : 0;
        }
    }
}
