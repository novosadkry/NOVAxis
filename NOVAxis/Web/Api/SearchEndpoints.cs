using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Web.Auth;
using NOVAxis.Web.Contracts;

namespace NOVAxis.Web.Api
{
    public static class SearchEndpoints
    {
        private const int DefaultLimit = 10;
        private const int MaxLimit = 25;

        public static IEndpointRouteBuilder MapSearchApi(this IEndpointRouteBuilder routes)
        {
            routes.MapGet("/api/guilds/{guildId}/search", Search)
                .RequireAuthorization()
                .RequireRateLimiting(WebRateLimits.Search);

            return routes;
        }

        private static async Task<IResult> Search(
            ulong guildId,
            string q,
            ClaimsPrincipal principal,
            GuildAccessService access,
            IAudioSearchService searchService,
            CancellationToken cancellationToken,
            int limit = DefaultLimit)
        {
            if (string.IsNullOrWhiteSpace(q))
                return WebApiErrors.BadRequest("Chybí co hledat");

            var member = await access.GetGuildUserAsync(guildId, principal.GetDiscordId());

            if (member == null)
                return WebApiErrors.NotMember();

            AudioLoadResult result;

            try
            {
                result = await searchService.SearchAsync(q, Math.Clamp(limit, 1, MaxLimit), cancellationToken);
            }
            catch (Exception e) when (e is ProcessException or HttpRequestException)
            {
                return WebApiErrors.ServiceUnavailable();
            }

            return Results.Ok(result.Tracks.Select(TrackDto.FromTrack).ToList());
        }
    }
}
