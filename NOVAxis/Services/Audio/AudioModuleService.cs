using System;
using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.Utilities;
using NOVAxis.Modules.Audio;

using Discord;
using Discord.WebSocket;

using Victoria;
using Victoria.EventArgs;

namespace NOVAxis.Services.Audio
{
    public class AudioModuleService
    {
        private DiscordShardedClient Client { get;}
        private ProgramConfig Config { get;}
        private LavaNode LavaNode { get;}
        private Cache<ulong, Lazy<AudioContext>> Guilds { get; }

        public AudioModuleService(DiscordShardedClient client, ProgramConfig config, LavaNode lavaNode)
        {
            Config = config;
            Guilds = new Cache<ulong, Lazy<AudioContext>>(
                Config.Audio.Cache.AbsoluteExpiration,
                Config.Audio.Cache.RelativeExpiration,
                (key, value, reason, state) =>
                {
                    if (value is Lazy<AudioContext> { IsValueCreated: true } context)
                        context.Value.Dispose();
                }
            );

            LavaNode = lavaNode;
            LavaNode.OnTrackEnded += AudioModuleService_TrackEnd;
            LavaNode.OnTrackStarted += AudioModuleService_TrackStart;

            Client = client;
            Client.UserVoiceStateUpdated += AudioModuleService_UserVoiceStateUpdated;
        }

        ~AudioModuleService()
        {
            LavaNode.OnTrackEnded -= AudioModuleService_TrackEnd;
            LavaNode.OnTrackStarted -= AudioModuleService_TrackStart;

            Client.UserVoiceStateUpdated -= AudioModuleService_UserVoiceStateUpdated;
        }

        public AudioContext this[ulong id]
        {
            get => Guilds.GetOrAdd(id, new Lazy<AudioContext>(() => new AudioContext(LavaNode, id))).Value;
            set => Guilds[id] = new Lazy<AudioContext>(value);
        }

        public void Remove(ulong id)
        {
            Guilds.Remove(id);
        }

        private async Task AudioModuleService_UserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            if (user.Id != Client.CurrentUser?.Id || before.VoiceChannel == null)
                return;

            if (LavaNode.HasPlayer(before.VoiceChannel.Guild))
            {
                if (after.VoiceChannel == null)
                {
                    Remove(before.VoiceChannel.Guild.Id);
                    await LavaNode.LeaveAsync(before.VoiceChannel);
                }
            }
        }

        private async Task AudioModuleService_TrackStart(TrackStartEventArgs args)
        {
            var audioContext = this[args.Player.VoiceChannel.GuildId];
            await audioContext.CancelDisconnectAsync();
        }

        private async Task AudioModuleService_TrackEnd(TrackEndedEventArgs args)
        {
            if (args.Player.VoiceChannel == null)
                return;

            var audioContext = this[args.Player.VoiceChannel.GuildId];

            if (!audioContext.Queue.Empty)
            {
                var prevTrack = audioContext.Queue.Dequeue();

                switch (audioContext.Repeat)
                {
                    case RepeatMode.Once:
                        audioContext.Queue.AddFirst(prevTrack);
                        audioContext.Repeat = RepeatMode.None;
                        break;

                    case RepeatMode.First:
                        audioContext.Queue.AddFirst(prevTrack);
                        break;

                    case RepeatMode.Queue:
                        audioContext.Queue.Enqueue(prevTrack);
                        break;
                }

                if (audioContext.Queue.Empty)
                {
                    await args.Player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                        .WithColor(52, 231, 231)
                        .WithTitle("Stream audia byl úspěšně dokončen").Build());

                    await audioContext.InitiateDisconnectAsync(args.Player, Config.Audio.Timeout.Idle);
                    return;
                }

                AudioTrack nextTrack = audioContext.Queue.Peek();
                await args.Player.PlayAsync(nextTrack);

                var statusEmoji = AudioModule.GetStatusEmoji(audioContext, args.Player);

                await args.Player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
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
                    .Build());
            }

            else
                await audioContext.InitiateDisconnectAsync(args.Player, Config.Audio.Timeout.Idle);
        }
    }
}
