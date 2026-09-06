using System;
using System.Linq;

using NOVAxis.Services.Audio;
using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Services.Polls;
using NOVAxis.Web.Contracts;

using Discord.WebSocket;

namespace NOVAxis.Web
{
    /// <summary>
    /// Reads a player into the shape the web player renders. Reads only - the queue
    /// hands out snapshots and the scalar properties are safe to read from any thread,
    /// so no state is taken from under the playback loop.
    /// </summary>
    public class PlayerStateService
    {
        private DiscordShardedClient Client { get; }
        private IAudioPlayerManager PlayerManager { get; }
        private SkipVoteService SkipVotes { get; }
        private SpectrumAnalyzer Spectrum { get; }

        public PlayerStateService(
            DiscordShardedClient client,
            IAudioPlayerManager playerManager,
            SkipVoteService skipVotes,
            SpectrumAnalyzer spectrum)
        {
            Client = client;
            PlayerManager = playerManager;
            SkipVotes = skipVotes;
            Spectrum = spectrum;
        }

        /// <summary>
        /// Whether a spectrum can be had here at all. It is the backend that decides, not
        /// the track: under Lavalink the audio is decoded on the node and this process
        /// never sees a sample, so the page must be told rather than left to infer it from
        /// frames that never arrive - silence looks the same.
        /// </summary>
        private bool CanAnalyze => Spectrum.Active && PlayerManager is YtDlpAudioPlayerManager;

        public PlayerStateDto GetState(ulong guildId)
        {
            if (!PlayerManager.TryGetPlayer(guildId, out var player))
                return PlayerStateDto.Disconnected(guildId, CanAnalyze);

            if (player.State == AudioPlayerState.Destroyed)
                return PlayerStateDto.Disconnected(guildId, CanAnalyze);

            var current = player.CurrentItem;
            var queue = player.Queue.Select(QueueItemDto.FromItem).ToList();
            var channel = Client.GetGuild(guildId)?.GetVoiceChannel(player.VoiceChannelId);

            return new PlayerStateDto(
                guildId.ToString(),
                true,
                player.State.ToString(),
                player.IsPaused,
                player.Volume,
                player.RepeatMode.ToString(),
                player.Position.TotalMilliseconds,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                channel == null ? null : new VoiceChannelDto(channel.Id.ToString(), channel.Name),
                QueueItemDto.FromItem(current),
                queue,
                SkipVoteDto.FromVote(SkipVotes.Peek(guildId, current?.RequestId)),
                CanAnalyze);
        }
    }
}
