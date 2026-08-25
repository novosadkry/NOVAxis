using System;
using System.Linq;

using NOVAxis.Services.Audio;
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

        public PlayerStateService(DiscordShardedClient client, IAudioPlayerManager playerManager)
        {
            Client = client;
            PlayerManager = playerManager;
        }

        public PlayerStateDto GetState(ulong guildId)
        {
            if (!PlayerManager.TryGetPlayer(guildId, out var player))
                return PlayerStateDto.Disconnected(guildId);

            if (player.State == AudioPlayerState.Destroyed)
                return PlayerStateDto.Disconnected(guildId);

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
                queue);
        }
    }
}
