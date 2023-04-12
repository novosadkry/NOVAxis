using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.Utilities;
using NOVAxis.Modules.Audio;

using Discord;
using Discord.WebSocket;

using Victoria.Player;
using Victoria.Node.EventArgs;

namespace NOVAxis.Services.Audio
{
    public class AudioService
    {
        private DiscordShardedClient Client { get;}
        private ProgramConfig Config { get;}
        private AudioNode AudioNode { get;}
        private Cache<ulong, AudioContext> Guilds { get; }

        public AudioService(DiscordShardedClient client, ProgramConfig config, AudioNode audioNode)
        {
            Config = config;
            Guilds = new Cache<ulong, AudioContext>(
                Config.Audio.Cache.AbsoluteExpiration,
                Config.Audio.Cache.RelativeExpiration
            );

            AudioNode = audioNode;
            AudioNode.OnTrackEnd += AudioModuleService_TrackEnd;
            AudioNode.OnTrackStart += AudioModuleService_TrackStart;

            Client = client;
            Client.UserVoiceStateUpdated += AudioModuleService_UserVoiceStateUpdated;
        }

        ~AudioService()
        {
            AudioNode.OnTrackEnd -= AudioModuleService_TrackEnd;
            AudioNode.OnTrackStart -= AudioModuleService_TrackStart;

            Client.UserVoiceStateUpdated -= AudioModuleService_UserVoiceStateUpdated;
        }

        public AudioContext this[ulong id]
        {
            get => Guilds.GetOrAdd(id, new AudioContext(AudioNode, id));
            set => Guilds[id] = value;
        }

        public void Remove(ulong id)
        {
            Guilds.Remove(id);
        }

        private async Task AudioModuleService_UserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            if (user.Id != Client.CurrentUser?.Id || before.VoiceChannel == null)
                return;

            if (AudioNode.HasPlayer(before.VoiceChannel.Guild))
            {
                if (after.VoiceChannel == null)
                {
                    Remove(before.VoiceChannel.Guild.Id);
                    await AudioNode.LeaveAsync(before.VoiceChannel);
                }
            }
        }

        private async Task AudioModuleService_TrackStart(TrackStartEventArg<AudioPlayer, LavaTrack> args)
        {
            var audioContext = this[args.Player.VoiceChannel.GuildId];
            await audioContext.CancelDisconnectAsync();
        }

        private async Task AudioModuleService_TrackEnd(TrackEndEventArg<AudioPlayer, LavaTrack> args)
        {
            var player = args.Player;
            var context = this[player.VoiceChannel.GuildId];

            if (player.VoiceChannel == null)
                return;

            if (context.Queue.TryDequeue(out var prevTrack))
            {
                switch (context.Repeat)
                {
                    case RepeatMode.Once:
                        context.Queue.AddFirst(prevTrack);
                        context.Repeat = RepeatMode.None;
                        break;

                    case RepeatMode.First:
                        context.Queue.AddFirst(prevTrack);
                        break;

                    case RepeatMode.Queue:
                        context.Queue.Enqueue(prevTrack);
                        break;
                }

                if (context.Queue.IsEmpty)
                {
                    await args.Player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithTitle("Stream audia byl úspěšně dokončen").Build());

                    await context.InitiateDisconnectAsync(args.Player, Config.Audio.Timeout.Idle);
                    return;
                }

                var nextTrack = context.Queue.Peek();
                await args.Player.PlayAsync(nextTrack);

                var statusEmoji = AudioModule.GetStatusEmoji(context, args.Player);

                var embed = new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{nextTrack.Title}")
                    .WithUrl(nextTrack.Url)
                    .WithThumbnailUrl(nextTrack.ThumbnailUrl)
                    .AddField("Autor:", nextTrack.Author, true)
                    .AddField("Délka:", $"`{nextTrack.Duration}`", true)
                    .AddField("Vyžádal:", nextTrack.RequestedBy.Mention, true)
                    .AddField("Hlasitost:", $"{args.Player.Volume}%", true)
                    .AddField("Stav:", $"{string.Join(' ', statusEmoji)}", true)
                    .Build();

                var components = new ComponentBuilder()
                    .WithButton(customId: "AudioControls_PlayPause", emote: new Emoji("\u23EF"))
                    .WithButton(customId: "AudioControls_Stop", emote: new Emoji("\u23F9"))
                    .WithButton(customId: "AudioControls_Skip", emote: new Emoji("\u23E9"))
                    .WithButton(customId: "AudioControls_Repeat", emote: new Emoji("\uD83D\uDD01"))
                    .WithButton(customId: "AudioControls_RepeatOnce", emote: new Emoji("\uD83D\uDD02"))
                    .WithButton(customId: "AudioControls_AddTrack", emote: new Emoji("\u2795"), style: ButtonStyle.Success)
                    .Build();

                await args.Player.TextChannel.SendMessageAsync(embed: embed, components: components);
            }

            else
                await context.InitiateDisconnectAsync(args.Player, Config.Audio.Timeout.Idle);
        }
    }
}
