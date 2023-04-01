using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Victoria.Node;
using Victoria.Player;

namespace NOVAxis.Services.Audio
{
    public class AudioNode : LavaNode<AudioPlayer, LavaTrack>
    {
        public AudioNode(DiscordSocketClient socketClient, NodeConfiguration nodeConfiguration, ILogger<AudioNode> logger)
            : base(socketClient, nodeConfiguration, logger) { }

        public AudioNode(DiscordShardedClient shardedClient, NodeConfiguration nodeConfiguration, ILogger<AudioNode> logger)
            : base(shardedClient, nodeConfiguration, logger) { }
    }
}
