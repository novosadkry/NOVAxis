using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NOVAxis.Services.Audio;
using NOVAxis.Web.Auth;
using NOVAxis.Web.Contracts;

using Discord.WebSocket;

namespace NOVAxis.Web.Api
{
    public static class GuildEndpoints
    {
        public static IEndpointRouteBuilder MapGuildApi(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/guilds")
                .RequireAuthorization();

            group.MapGet("/", ListGuilds);
            group.MapGet("/{guildId}/state", GetState);

            return routes;
        }

        /// <summary>
        /// The guilds the caller and the bot have in common - the only ones the
        /// web player can show anything for.
        /// </summary>
        private static async Task<IResult> ListGuilds(
            ClaimsPrincipal principal,
            DiscordShardedClient client,
            GuildAccessService access,
            IAudioPlayerManager playerManager)
        {
            var userId = principal.GetDiscordId();

            var lookups = await Task.WhenAll(client.Guilds
                .Select(async guild => new
                {
                    Guild = guild,
                    Member = await access.GetGuildUserAsync(guild.Id, userId)
                }));

            var guilds = lookups
                .Where(x => x.Member != null)
                .Select(x => new GuildDto(
                    x.Guild.Id.ToString(),
                    x.Guild.Name,
                    x.Guild.IconUrl,
                    playerManager.TryGetPlayer(x.Guild.Id, out _)))
                .ToList();

            return Results.Ok(guilds);
        }

        private static async Task<IResult> GetState(
            ulong guildId,
            ClaimsPrincipal principal,
            GuildAccessService access,
            PlayerStateService state)
        {
            var member = await access.GetGuildUserAsync(guildId, principal.GetDiscordId());

            if (member == null)
                return WebApiErrors.NotMember();

            return Results.Ok(state.GetState(guildId));
        }
    }
}
