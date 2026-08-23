using System;
using System.Threading;
using System.Threading.Tasks;

using Discord;
using Microsoft.Extensions.DependencyInjection;

using Lavalink4NET.Players;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.InactivityTracking.Players;
using Lavalink4NET.InactivityTracking.Trackers;

namespace NOVAxis.Services.Audio.Lavalink
{
    public record LavalinkAudioPlayerOptions : QueuedLavalinkPlayerOptions
    {
        public ITextChannel TextChannel { get; set; }
    }

    /// <summary>
    /// The Lavalink backed player. Playback, queueing and seeking are handled by the node,
    /// this type only maps between Lavalink4NET's model and <see cref="IAudioPlayer"/>.
    /// </summary>
    public class LavalinkAudioPlayer : QueuedLavalinkPlayer, IAudioPlayer, IInactivityPlayerListener
    {
        private readonly LavalinkAudioTrackQueue _queue;

        private ITextChannel TextChannel { get; }
        private IDiscordClient Client { get; }
        private AudioNotifier Notifier { get; }

        public LavalinkAudioPlayer(IPlayerProperties<LavalinkAudioPlayer, LavalinkAudioPlayerOptions> properties)
            : base(properties)
        {
            _queue = new LavalinkAudioTrackQueue(base.Queue);

            TextChannel = properties.Options.Value.TextChannel;
            Client = properties.ServiceProvider!.GetRequiredService<IDiscordClient>();
            Notifier = properties.ServiceProvider!.GetRequiredService<AudioNotifier>();
        }

        IAudioTrackQueue IAudioPlayer.Queue => _queue;

        AudioTrackQueueItem IAudioPlayer.CurrentItem => LavalinkTrackQueueItem.Unwrap(CurrentItem);

        AudioTrack IAudioPlayer.CurrentTrack => ((IAudioPlayer)this).CurrentItem?.Track;

        TimeSpan IAudioPlayer.Position => Position?.Position ?? TimeSpan.Zero;

        AudioPlayerState IAudioPlayer.State => State switch
        {
            PlayerState.Playing => AudioPlayerState.Playing,
            PlayerState.Paused => AudioPlayerState.Paused,
            PlayerState.NotPlaying => AudioPlayerState.NotPlaying,
            _ => AudioPlayerState.Destroyed
        };

        AudioRepeatMode IAudioPlayer.RepeatMode
        {
            get => RepeatMode switch
            {
                TrackRepeatMode.Track => AudioRepeatMode.Track,
                TrackRepeatMode.Queue => AudioRepeatMode.Queue,
                _ => AudioRepeatMode.None
            };

            set => RepeatMode = value switch
            {
                AudioRepeatMode.Track => TrackRepeatMode.Track,
                AudioRepeatMode.Queue => TrackRepeatMode.Queue,
                _ => TrackRepeatMode.None
            };
        }

        async ValueTask IAudioPlayer.PlayAsync(AudioTrackQueueItem item, bool enqueue, CancellationToken cancellationToken)
        {
            await PlayAsync(new LavalinkTrackQueueItem(item), enqueue, cancellationToken: cancellationToken);
        }

        async ValueTask IAudioPlayer.StopAsync(CancellationToken cancellationToken)
        {
            // Lavalink's StopAsync leaves the queue behind, ours is expected to clear it
            await base.Queue.ClearAsync(cancellationToken);
            await StopAsync(cancellationToken);
        }

        protected override async ValueTask NotifyTrackStartedAsync(
            ITrackQueueItem queueItem,
            CancellationToken cancellationToken = default)
        {
            await base.NotifyTrackStartedAsync(queueItem, cancellationToken);

            var item = LavalinkTrackQueueItem.Unwrap(queueItem);
            await Notifier.TrackStartedAsync(TextChannel, item, IsPaused, Volume, base.Queue.Count);
        }

        protected override async ValueTask NotifyTrackEnqueuedAsync(
            ITrackQueueItem queueItem, int position,
            CancellationToken cancellationToken = default)
        {
            await base.NotifyTrackEnqueuedAsync(queueItem, position, cancellationToken);

            var item = LavalinkTrackQueueItem.Unwrap(queueItem);
            await Notifier.TrackEnqueuedAsync(TextChannel, item, position);
        }

        protected override async ValueTask NotifyTrackExceptionAsync(
            ITrackQueueItem queueItem,
            TrackException exception,
            CancellationToken cancellationToken = default)
        {
            await base.NotifyTrackExceptionAsync(queueItem, exception, cancellationToken);

            var item = LavalinkTrackQueueItem.Unwrap(queueItem);
            await Notifier.TrackExceptionAsync(TextChannel, item, new InvalidOperationException(exception.Message));
        }

        public async ValueTask NotifyPlayerInactiveAsync(
            PlayerTrackingState trackingState,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var voiceChannel = await ((IAudioPlayer)this).GetVoiceChannel(Client);
            await Notifier.PlayerInactiveAsync(TextChannel, voiceChannel.Name);
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

        public static ValueTask<LavalinkAudioPlayer> CreatePlayerAsync(
            IPlayerProperties<LavalinkAudioPlayer, LavalinkAudioPlayerOptions> properties,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(properties);

            return ValueTask.FromResult(new LavalinkAudioPlayer(properties));
        }
    }
}
