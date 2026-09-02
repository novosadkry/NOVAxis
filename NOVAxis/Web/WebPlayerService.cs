using System;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;

using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Utilities;
using NOVAxis.Web.Api;
using NOVAxis.Web.Auth;
using NOVAxis.Web.Contracts;

namespace NOVAxis.Web
{
    /// <summary>
    /// Carries out playback commands for the web player. The same rules apply as to
    /// the slash commands: the caller has to sit in the bot's voice channel, and every
    /// action goes through the player manager's own gates and preconditions.
    /// </summary>
    public class WebPlayerService
    {
        private GuildAccessService Access { get; }
        private IAudioPlayerManager PlayerManager { get; }
        private IAudioSearchService SearchService { get; }
        private PlayerBroadcaster Broadcaster { get; }

        public WebPlayerService(
            GuildAccessService access,
            IAudioPlayerManager playerManager,
            IAudioSearchService searchService,
            PlayerBroadcaster broadcaster)
        {
            Access = access;
            PlayerManager = playerManager;
            SearchService = searchService;
            Broadcaster = broadcaster;
        }

        /// <summary>
        /// Runs <paramref name="action"/> against the guild's existing player and pushes
        /// the new state to everyone watching.
        /// </summary>
        public async Task<IResult> ControlAsync(
            ClaimsPrincipal principal,
            ulong guildId,
            Func<IAudioPlayer, ValueTask> action,
            params AudioPrecondition[] preconditions)
        {
            var user = await Access.GetGuildUserAsync(guildId, principal.GetDiscordId());

            if (user == null)
                return WebApiErrors.NotMember();

            var options = new AudioPlayerRetrieveOptions
            {
                JoinChannel = false,
                RequireSameChannel = true,
                Preconditions = preconditions
            };

            var result = await PlayerManager.RetrieveAsync(user, null, options);

            if (result.Status != AudioPlayerRetrieveStatus.Success)
                return WebApiErrors.From(result);

            await action(result.Player);
            await Broadcaster.PushAsync(guildId);

            return Results.NoContent();
        }

        /// <summary>
        /// The web counterpart of "/audio play" - joins the caller's channel when
        /// needed, resolves the query and queues what it found.
        /// </summary>
        public async Task<IResult> PlayAsync(
            ClaimsPrincipal principal,
            ulong guildId,
            string query,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                return WebApiErrors.BadRequest("Chybí co přehrát");

            var user = await Access.GetGuildUserAsync(guildId, principal.GetDiscordId());

            if (user == null)
                return WebApiErrors.NotMember();

            // The extractor is the slow half and needs nothing from the player, so it runs
            // while the bot is still joining - otherwise the queue only gains the track
            // seconds after the browser has already seen the bot connect
            var lookup = SearchService.LoadAsync(query, cancellationToken).AsTask();

            // The retrieve below may still turn the request away, leaving this awaited by
            // nobody - and a failure with no observer is one raised at a finalizer instead
            _ = lookup.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

            var options = new AudioPlayerRetrieveOptions
            {
                JoinChannel = true,
                RequireSameChannel = true
            };

            var result = await PlayerManager.RetrieveAsync(user, null, options, cancellationToken);

            if (result.Status != AudioPlayerRetrieveStatus.Success)
                return WebApiErrors.From(result);

            AudioLoadResult load;

            try
            {
                load = await lookup;
            }
            catch (Exception e) when (e is ProcessException or HttpRequestException)
            {
                return WebApiErrors.ServiceUnavailable();
            }

            if (load.IsFailed)
                return WebApiErrors.NothingFound();

            var items = load.Tracks
                .Select(track => new AudioTrackQueueItem
                {
                    Track = track,
                    RequestedBy = user,
                    RequestId = Snowflake.Next()
                })
                .ToList();

            await result.Player.PlayAsync(items[0]);

            if (items.Count > 1)
                await result.Player.Queue.AddRangeAsync(items.Skip(1));

            await Broadcaster.PushAsync(guildId);

            return Results.Ok(new PlayResponse(items.Count, TrackDto.FromTrack(load.Track), load.Playlist?.Name));
        }

        /// <summary>
        /// Builds an item which is equal to the queued one - entries compare by
        /// <see cref="AudioTrackQueueItem.RequestId"/> alone.
        /// </summary>
        public static AudioTrackQueueItem Probe(ulong requestId)
            => new() { RequestId = requestId };
    }
}
