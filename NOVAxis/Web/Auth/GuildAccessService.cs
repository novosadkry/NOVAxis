using System;
using System.Net;
using System.Threading.Tasks;

using Microsoft.Extensions.Caching.Memory;

using Discord;
using Discord.Net;
using Discord.Rest;
using Discord.WebSocket;

namespace NOVAxis.Web.Auth
{
    /// <summary>
    /// Decides what a logged-in user may touch. The browser only proves who the user is;
    /// whether they belong to a guild is answered by the bot itself, never by the client.
    /// </summary>
    public class GuildAccessService
    {
        /// <summary>
        /// How long a membership lookup stays answered. A member who leaves keeps
        /// their access for at most this long.
        /// </summary>
        private static readonly TimeSpan MembershipTtl = TimeSpan.FromMinutes(1);

        private DiscordShardedClient Client { get; }
        private DiscordRestClient RestClient { get; }
        private IMemoryCache Cache { get; }

        public GuildAccessService(DiscordShardedClient client, DiscordRestClient restClient, IMemoryCache cache)
        {
            Client = client;
            RestClient = restClient;
            Cache = cache;
        }

        /// <summary>
        /// Resolves the user's membership of a guild, or null when there is none.
        /// The gateway cache answers first and knows voice states; the REST fallback
        /// proves membership for members the gateway has not cached.
        /// </summary>
        public async ValueTask<IGuildUser> GetGuildUserAsync(ulong guildId, ulong userId)
        {
            if (userId == 0)
                return null;

            var key = $"{nameof(GuildAccessService)}:{guildId}:{userId}";

            if (Cache.TryGetValue(key, out IGuildUser cached))
                return cached;

            var user = await ResolveAsync(guildId, userId);
            Cache.Set(key, user, MembershipTtl);

            return user;
        }

        private async Task<IGuildUser> ResolveAsync(ulong guildId, ulong userId)
        {
            var guild = Client.GetGuild(guildId);

            if (guild == null)
                return null;

            IGuildUser user = guild.GetUser(userId);

            if (user != null)
                return user;

            try
            {
                return await RestClient.GetGuildUserAsync(guildId, userId);
            }
            catch (HttpException e) when (e.HttpCode == HttpStatusCode.NotFound)
            {
                return null;
            }
        }
    }
}
