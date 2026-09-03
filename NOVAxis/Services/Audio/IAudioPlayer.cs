using System;
using System.Threading;
using System.Threading.Tasks;

using Discord;

namespace NOVAxis.Services.Audio
{
    public enum AudioPlayerState
    {
        Destroyed,
        NotPlaying,
        Playing,
        Paused
    }

    public enum AudioRepeatMode
    {
        None,
        Track,
        Queue
    }

    /// <summary>
    /// A guild scoped audio player. Every guild owns exactly one instance, which in turn
    /// owns its voice connection, its queue and any process spawned on its behalf.
    /// </summary>
    public interface IAudioPlayer : IAsyncDisposable
    {
        ulong GuildId { get; }
        ulong VoiceChannelId { get; }

        /// <summary>
        /// Where this player answers - the channel it was summoned from. Anything which
        /// has to say something to the room, rather than to whoever asked, sends it here.
        /// </summary>
        ITextChannel TextChannel { get; }

        IAudioTrackQueue Queue { get; }
        AudioTrackQueueItem CurrentItem { get; }
        AudioTrack CurrentTrack { get; }

        AudioPlayerState State { get; }
        bool IsPaused { get; }
        float Volume { get; }
        TimeSpan Position { get; }
        AudioRepeatMode RepeatMode { get; set; }

        /// <summary>
        /// Starts playing <paramref name="item"/>. When <paramref name="enqueue"/> is set and
        /// something is already playing, the item is appended to the queue instead.
        /// </summary>
        ValueTask PlayAsync(AudioTrackQueueItem item, bool enqueue = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Skips <paramref name="count"/> tracks. The first one is the track being played.
        /// </summary>
        ValueTask SkipAsync(int count = 1, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the current track and clears the queue, keeping the voice connection alive.
        /// </summary>
        ValueTask StopAsync(CancellationToken cancellationToken = default);

        ValueTask PauseAsync(CancellationToken cancellationToken = default);
        ValueTask ResumeAsync(CancellationToken cancellationToken = default);
        ValueTask SeekAsync(TimeSpan position, CancellationToken cancellationToken = default);
        ValueTask SetVolumeAsync(float volume, CancellationToken cancellationToken = default);

        /// <summary>
        /// Leaves the voice channel and releases every resource held by the player.
        /// </summary>
        ValueTask DisconnectAsync(CancellationToken cancellationToken = default);
    }

    public static class AudioPlayerExtensions
    {
        public static async ValueTask<IVoiceChannel> GetVoiceChannel(this IAudioPlayer player, IDiscordClient client)
        {
            var guild = await client.GetGuildAsync(player.GuildId);
            return await guild.GetVoiceChannelAsync(player.VoiceChannelId);
        }
    }
}
