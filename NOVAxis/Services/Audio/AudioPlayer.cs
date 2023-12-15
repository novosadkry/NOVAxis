using System;
using System.Threading;
using System.Threading.Tasks;

using NOVAxis.Utilities;

using Discord;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

using Lavalink4NET.Tracks;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.InactivityTracking.Players;
using Lavalink4NET.InactivityTracking.Trackers;

namespace NOVAxis.Services.Audio
{
    public static class LavalinkPlayerExtensions
    {
        public static async ValueTask<IVoiceChannel> GetVoiceChannel(this ILavalinkPlayer player, IDiscordClient client)
        {
            var guild = await client.GetGuildAsync(player.GuildId);
            return await guild.GetVoiceChannelAsync(player.VoiceChannelId);
        }
    }

    public class AudioPlayer : QueuedLavalinkPlayer, IInactivityPlayerListener
    {
        private ITextChannel TextChannel { get; }
        private IDiscordClient Client { get; }
        private ILogger<AudioPlayer> Logger { get; }
        private InteractionCache InteractionCache { get; }

        public AudioPlayer(IPlayerProperties<AudioPlayer, AudioPlayerOptions> properties)
            : base(properties)
        {
            TextChannel = properties.Options.Value.TextChannel;
            Client = properties.ServiceProvider!.GetRequiredService<IDiscordClient>();
            Logger = properties.ServiceProvider!.GetRequiredService<ILogger<AudioPlayer>>();
            InteractionCache = properties.ServiceProvider!.GetRequiredService<InteractionCache>();
        }

        protected override async ValueTask NotifyTrackStartedAsync(
            ITrackQueueItem queueItem,
            CancellationToken cancellationToken = default)
        {
            await base.NotifyTrackStartedAsync(queueItem, cancellationToken);
            await OnTrackStarted(queueItem as AudioTrackQueueItem);
        }

        protected override async ValueTask NotifyTrackEnqueuedAsync(
            ITrackQueueItem queueItem, int position,
            CancellationToken cancellationToken = default)
        {
            await base.NotifyTrackEnqueuedAsync(queueItem, position, cancellationToken);
            await OnTrackEnqueued(queueItem as AudioTrackQueueItem, position);
        }

        private async ValueTask OnTrackEnqueued(AudioTrackQueueItem item, int position)
        {
            var id = InteractionCache.Store(item);
            var track = item.Track!;

            var embed = new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithAuthor("Přidáno do fronty:")
                .WithTitle($"{track.Title}")
                .WithUrl(track.Uri?.AbsoluteUri)
                .WithThumbnailUrl(track.ArtworkUri?.AbsoluteUri)
                .AddField("Autor:", track.Author, true)
                .AddField("Délka:", $"`{track.Duration}`", true)
                .AddField("Vyžádal:", item.RequestedBy.Mention, true)
                .AddField("Pořadí ve frontě:", $"`{position}.`", true)
                .Build();

            var components = new ComponentBuilder()
                .WithButton(customId: $"TrackControls_Remove,{id}", emote: new Emoji("\u2716"), style: ButtonStyle.Danger)
                .WithButton(customId: $"TrackControls_Add,{track.Identifier}", emote: new Emoji("\u2764"), style: ButtonStyle.Secondary)
                .WithButton(customId: "TrackControls_Add", emote: new Emoji("\u2795"), style: ButtonStyle.Success)
                .Build();

            await TextChannel.SendMessageAsync(embed: embed, components: components);
        }

        private async ValueTask OnTrackStarted(AudioTrackQueueItem item)
        {
            var id = InteractionCache.Store(item);
            var track = item.Track!;

            var statusEmoji = !IsPaused
                ? new Emoji("\u25B6") // Playing
                : new Emoji("\u23F8"); // Paused

            var embed = new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithAuthor("Právě přehrávám:")
                .WithTitle($"{track.Title}")
                .WithUrl(track.Uri?.AbsoluteUri)
                .WithThumbnailUrl(track.ArtworkUri?.AbsoluteUri)
                .AddField("Autor:", track.Author, true)
                .AddField("Délka:", $"`{track.Duration}`", true)
                .AddField("Vyžádal:", item.RequestedBy.Mention, true)
                .AddField("Hlasitost:", $"{Volume * 100.0f}%", true)
                .AddField("Stav:", $"{statusEmoji}", true)
                .Build();

            var components = new ComponentBuilder()
                .WithButton(customId: $"TrackControls_Remove,{id}", emote: new Emoji("\u2716"), style: ButtonStyle.Danger)
                .WithButton(customId: $"TrackControls_Add,{track.Identifier}", emote: new Emoji("\u2764"), style: ButtonStyle.Secondary)
                .WithButton(customId: "TrackControls_Add", emote: new Emoji("\u2795"), style: ButtonStyle.Success)
                .Build();

            await TextChannel.SendMessageAsync(embed: embed, components: components);
        }

        protected override async ValueTask NotifyTrackExceptionAsync(
            LavalinkTrack track,
            TrackException exception,
            CancellationToken cancellationToken = default)
        {
            await base.NotifyTrackExceptionAsync(track, exception, cancellationToken);

            await TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                .WithColor(220, 20, 60)
                .WithDescription("(Skladba přeskočena)")
                .WithTitle("Při přehrávání stopy nastala kritická chyba")
                .Build());

            Logger.LogError("Track failed to start, throwing an exception before providing any audio");
        }

        public async ValueTask NotifyPlayerInactiveAsync(
            PlayerTrackingState trackingState,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var guild = await Client.GetGuildAsync(GuildId);
            var voiceChannel = await guild.GetVoiceChannelAsync(VoiceChannelId);

            await TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Odpojuji se od kanálu `{voiceChannel.Name}`").Build());
        }

        public ValueTask NotifyPlayerActiveAsync(
            PlayerTrackingState trackingState,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        public ValueTask NotifyPlayerTrackedAsync(
            PlayerTrackingState trackingState,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        public static ValueTask<AudioPlayer> CreatePlayerAsync(
            IPlayerProperties<AudioPlayer, AudioPlayerOptions> properties,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(properties);

            return ValueTask.FromResult(new AudioPlayer(properties));
        }
    }

    public record AudioPlayerOptions : QueuedLavalinkPlayerOptions
    {
        public ITextChannel TextChannel { get; set; }
    }
}
