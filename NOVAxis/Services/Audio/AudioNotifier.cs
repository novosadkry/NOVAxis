using System;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Utilities;
using NOVAxis.Extensions;

using Discord;

namespace NOVAxis.Services.Audio
{
    /// <summary>
    /// Posts playback updates into the text channel a player was created from.
    /// A failing notification must never take playback down with it, so every send
    /// is guarded and only logged.
    /// </summary>
    public class AudioNotifier
    {
        private InteractionCache InteractionCache { get; }
        private IOptions<WebOptions> WebOptions { get; }
        private ILogger<AudioNotifier> Logger { get; }

        public AudioNotifier(InteractionCache interactionCache, IOptions<WebOptions> webOptions, ILogger<AudioNotifier> logger)
        {
            InteractionCache = interactionCache;
            WebOptions = webOptions;
            Logger = logger;
        }

        public Task TrackEnqueuedAsync(ITextChannel channel, AudioTrackQueueItem item, int position)
        {
            var id = InteractionCache.Store(item);
            var webUrl = channel != null ? WebOptions.Value.GetPlayerUrl(channel.GuildId) : null;

            return SendAsync(channel,
                AudioEmbeds.TrackEnqueued(item, position),
                AudioEmbeds.TrackControls(id, item.Track, webUrl));
        }

        public Task TrackStartedAsync(ITextChannel channel, AudioTrackQueueItem item, bool isPaused, float volume, int queueCount)
        {
            var id = InteractionCache.Store(item);
            var webUrl = channel != null ? WebOptions.Value.GetPlayerUrl(channel.GuildId) : null;

            return SendAsync(channel,
                AudioEmbeds.NowPlaying(item, isPaused, volume, queueCount),
                AudioEmbeds.TrackControls(id, item.Track, webUrl));
        }

        public Task TrackExceptionAsync(ITextChannel channel, AudioTrackQueueItem item, Exception exception)
        {
            Logger.Error($"Track '{item?.Track?.Title}' failed to play", exception);

            return SendAsync(channel, AudioEmbeds.Error(
                "Při přehrávání stopy nastala neznámá chyba",
                "(Skladba přeskočena)"));
        }

        public Task PlayerInactiveAsync(ITextChannel channel, string voiceChannelName)
        {
            return SendAsync(channel, AudioEmbeds.Info($"Odpojuji se od kanálu `{voiceChannelName}`"));
        }

        private async Task SendAsync(ITextChannel channel, Embed embed, MessageComponent components = null)
        {
            if (channel == null)
                return;

            try
            {
                await channel.SendMessageAsync(embed: embed, components: components);
            }
            catch (Exception e)
            {
                Logger.Warning("Unable to deliver an audio notification", e);
            }
        }
    }
}
