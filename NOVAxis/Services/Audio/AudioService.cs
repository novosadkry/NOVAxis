using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.Utilities;
using NOVAxis.Modules.Audio;

using Discord;
using Discord.WebSocket;

using Victoria.Player;
using Victoria.Node.EventArgs;
using System;
using Microsoft.Extensions.Logging;

namespace NOVAxis.Services.Audio
{
    public class AudioService
    {
        private DiscordShardedClient Client { get; }
        private ProgramConfig Config { get; }
        private AudioNode AudioNode { get; }
        private ILogger<AudioService> Logger { get; }
        private Cache<ulong, AudioContext> Guilds { get; }
        private Cache<ulong, object> InteractionCache { get; }

        public AudioService(
            DiscordShardedClient client, 
            ProgramConfig config,
            AudioNode audioNode,
            ILogger<AudioService> logger,
            Cache<ulong, object> interactionCache)
        {
            Config = config;
            Logger = logger;

            Guilds = new Cache<ulong, AudioContext>(
                Config.Audio.Cache.AbsoluteExpiration,
                Config.Audio.Cache.RelativeExpiration
            );

            AudioNode = audioNode;
            AudioNode.OnTrackEnd += AudioModuleService_TrackEnd;
            AudioNode.OnTrackStart += AudioModuleService_TrackStart;

            Client = client;
            Client.UserVoiceStateUpdated += AudioModuleService_UserVoiceStateUpdated;

            InteractionCache = interactionCache;
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
            var player = args.Player;
            var context = this[player.VoiceChannel.GuildId];

            var track = context.Track;
            await context.CancelDisconnectAsync();

            if (context.Queue.Count > 1)
            {
                var id = SnowflakeUtils.ToSnowflake(DateTimeOffset.Now);
                InteractionCache[id] = track;

                var statusEmoji = AudioModule.GetStatusEmoji(context, args.Player);

                var embed = new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{track.Title}")
                    .WithUrl(track.Url)
                    .WithThumbnailUrl(track.ThumbnailUrl)
                    .AddField("Autor:", track.Author, true)
                    .AddField("Délka:", $"`{track.Duration}`", true)
                    .AddField("Vyžádal:", track.RequestedBy.Mention, true)
                    .AddField("Hlasitost:", $"{args.Player.Volume}%", true)
                    .AddField("Stav:", $"{string.Join(' ', statusEmoji)}", true)
                    .Build();

                var components = new ComponentBuilder()
                    .WithButton(customId: $"TrackControls_Remove,{id}", emote: new Emoji("\u2716"), style: ButtonStyle.Danger)
                    .WithButton(customId: $"TrackControls_Add,{track.Url}", emote: new Emoji("\u2764"), style: ButtonStyle.Secondary)
                    .WithButton(customId: "TrackControls_Add", emote: new Emoji("\u2795"), style: ButtonStyle.Success)
                    .Build();

                await args.Player.TextChannel.SendMessageAsync(embed: embed, components: components);
            }
        }

        private async Task AudioModuleService_TrackEnd(TrackEndEventArg<AudioPlayer, LavaTrack> args)
        {
            var player = args.Player;
            var context = this[player.VoiceChannel.GuildId];

            if (player.VoiceChannel == null)
                return;

            if (context.Queue.TryDequeue(out var prevTrack))
            {
                if (args.Reason == TrackEndReason.LoadFailed)
                {
                    await args.Player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Skladba přeskočena)")
                        .WithTitle("Při přehrávání stopy nastala kritická chyba")
                        .Build());

                    Logger.LogError("Track failed to start, throwing an exception before providing any audio");
                }

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
                    await context.InitiateDisconnectAsync(args.Player, Config.Audio.Timeout.Idle);
                    return;
                }

                var nextTrack = context.Queue.Peek();
                await args.Player.PlayAsync(nextTrack);
            }

            else
                await context.InitiateDisconnectAsync(args.Player, Config.Audio.Timeout.Idle);
        }
    }
}
