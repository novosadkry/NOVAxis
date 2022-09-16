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
        public ProgramConfig.AudioObject AudioConfig { get; }

        private readonly LavaNode _lavaNodeInstance;
        private readonly Cache<ulong, Lazy<AudioContext>> _guilds;

        public AudioModuleService(LavaNode lavaNodeInstance)
        {
            AudioConfig = Program.Config.Audio;
            _guilds = new Cache<ulong, Lazy<AudioContext>>(
                AudioConfig.Cache.AbsoluteExpiration,
                AudioConfig.Cache.RelativeExpiration,
                (key, value, reason, state) =>
                {
                    if (value is Lazy<AudioContext> { IsValueCreated: true } context)
                        context.Value.Dispose();
                }
            );

            _lavaNodeInstance = lavaNodeInstance;
            _lavaNodeInstance.OnTrackEnded += AudioModuleService_TrackEnd;
            _lavaNodeInstance.OnTrackStarted += AudioModuleService_TrackStart;

            Program.Client.UserVoiceStateUpdated += AudioModuleService_UserVoiceStateUpdated;
        }

        ~AudioModuleService()
        {
            _lavaNodeInstance.OnTrackEnded -= AudioModuleService_TrackEnd;
            _lavaNodeInstance.OnTrackStarted -= AudioModuleService_TrackStart;

            Program.Client.UserVoiceStateUpdated -= AudioModuleService_UserVoiceStateUpdated;
        }

        public AudioContext this[ulong id]
        {
            get => _guilds.GetOrAdd(id, new Lazy<AudioContext>(() => new AudioContext(_lavaNodeInstance, id))).Value;
            set => _guilds[id] = new Lazy<AudioContext>(value);
        }

        public void Remove(ulong id)
        {
            _guilds.Remove(id);
        }

        private async Task AudioModuleService_UserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            if (user.Id != Program.Client.CurrentUser?.Id || before.VoiceChannel == null)
                return;

            if (_lavaNodeInstance.HasPlayer(before.VoiceChannel.Guild))
            {
                if (after.VoiceChannel == null)
                {
                    Remove(before.VoiceChannel.Guild.Id);
                    await _lavaNodeInstance.LeaveAsync(before.VoiceChannel);
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

                    await audioContext.InitiateDisconnectAsync(args.Player, AudioConfig.Timeout.Idle);
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
                await audioContext.InitiateDisconnectAsync(args.Player, AudioConfig.Timeout.Idle);
        }
    }
}
