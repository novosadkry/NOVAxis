using System;
using System.Security.Claims;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

using NOVAxis.Core;
using NOVAxis.Web.Api;
using NOVAxis.Web.Hubs;

namespace NOVAxis.Web
{
    /// <summary>
    /// The request pipeline of the web player - static frontend, the api, and the hub.
    /// </summary>
    public static class WebPipeline
    {
        public static void Configure(IApplicationBuilder app)
        {
            app.UseForwardedHeaders();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseRateLimiter();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/api/health", ()
                    => Results.Ok(new { Status = "ok", Version = Program.Version }));

                endpoints.MapAuthApi();
                endpoints.MapGuildApi();
                endpoints.MapPlayerApi();
                endpoints.MapSearchApi();
                endpoints.MapDownloadApi();
                endpoints.MapPlaylistApi();

                endpoints.MapHub<PlayerHub>("/hub/player");

                // Deep links like /g/123 are the frontend's to route
                endpoints.MapFallbackToFile("index.html");
            });
        }
    }

    /// <summary>
    /// Per-user ceilings over the write and search endpoints, the web counterpart
    /// of the cooldowns the slash commands carry.
    /// </summary>
    public static class WebRateLimits
    {
        public const string Write = "web-write";
        public const string Search = "web-search";

        /// <summary>
        /// Defence in depth, not the quota itself: the slash commands never pass through
        /// the rate limiter, so a ceiling kept here alone could be doubled by using both
        /// surfaces. The counter that actually decides lives in DownloadService.
        /// </summary>
        public const string Download = "web-download";

        public static IServiceCollection AddWebRateLimits(this IServiceCollection collection)
        {
            collection.AddRateLimiter(options =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                // Without this a throttled request answers with an empty body, and the
                // frontend has nothing to show but its generic "something went wrong"
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                    await context.HttpContext.Response.WriteAsJsonAsync(
                        new ErrorDto("rate_limited", "Moc rychle po sobě, zkus to za chvíli"),
                        cancellationToken);
                };

                options.AddPolicy(Write, context => RateLimitPartition.GetFixedWindowLimiter(
                    Caller(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(10),
                        PermitLimit = 10,
                        QueueLimit = 0
                    }));

                options.AddPolicy(Download, context => RateLimitPartition.GetFixedWindowLimiter(
                    Caller(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromHours(1),
                        PermitLimit = 10,
                        QueueLimit = 0
                    }));

                options.AddPolicy(Search, context => RateLimitPartition.GetFixedWindowLimiter(
                    Caller(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        Window = TimeSpan.FromSeconds(10),
                        PermitLimit = 5,
                        QueueLimit = 0
                    }));
            });

            return collection;
        }

        private static string Caller(HttpContext context)
        {
            return context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";
        }
    }
}
