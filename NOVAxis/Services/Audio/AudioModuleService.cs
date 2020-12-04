using System.Collections.Concurrent;
using System.Threading.Tasks;

using NOVAxis.Core;

using Discord;
using Discord.WebSocket;

using Victoria;
using Victoria.Enums;
using Victoria.EventArgs;

namespace NOVAxis.Services.Audio
{
    public class AudioModuleService
    {
        public long AudioTimeout { get; }

        private readonly LavaNode _lavaNodeInstance;
        private readonly ConcurrentDictionary<ulong, AudioContext> _guilds;

        public AudioModuleService(LavaNode lavaNodeInstance)
        {
            AudioTimeout = Program.Config.AudioTimeout;
            _guilds = new ConcurrentDictionary<ulong, AudioContext>();

            _lavaNodeInstance = lavaNodeInstance;
            _lavaNodeInstance.OnTrackEnded += AudioModuleService_TrackEnd;

            Program.Client.UserVoiceStateUpdated += AudioModuleService_UserVoiceStateUpdated;
        }

        ~AudioModuleService()
        {
            _lavaNodeInstance.OnTrackEnded -= AudioModuleService_TrackEnd;
            Program.Client.UserVoiceStateUpdated -= AudioModuleService_UserVoiceStateUpdated;
        }

        public AudioContext this[ulong id]
        {
            get => _guilds.GetOrAdd(id, new AudioContext(id));
            set => _guilds[id] = value;
        }

        private async Task AudioModuleService_UserVoiceStateUpdated(SocketUser user, SocketVoiceState before, SocketVoiceState after)
        {
            if (user != Program.Client.CurrentUser || before.VoiceChannel == null)
                return;

            if (_lavaNodeInstance.TryGetPlayer(before.VoiceChannel.Guild, out LavaPlayer player))
            {
                if (after.VoiceChannel == null && player.PlayerState == PlayerState.Playing)
                {
                    AudioContext context = this[before.VoiceChannel.Guild.Id];

                    context.Queue.Clear();
                    context.Timer.Dispose();

                    await _lavaNodeInstance.LeaveAsync(before.VoiceChannel);
                }
            }
        }

        private async Task AudioModuleService_TrackEnd(TrackEndedEventArgs args)
        {
            if (args.Player.VoiceChannel == null)
                return;

            var service = _guilds[args.Player.VoiceChannel.GuildId];

            if (service.Queue.Count == 0)
                return;

            var prevTrack = service.Queue.Dequeue();

            switch (service.Repeat)
            {
                case RepeatMode.Once:
                    service.Queue.AddFirst(prevTrack);
                    service.Repeat = RepeatMode.None;
                    break;

                case RepeatMode.First:
                    service.Queue.AddFirst(prevTrack);
                    break;

                case RepeatMode.Queue:
                    service.Queue.Enqueue(prevTrack);
                    break;
            }

            if (service.Queue.Count > 0)
            {
                AudioTrack nextTrack = service.Queue.Peek();
                await args.Player.PlayAsync(nextTrack);

                object[] statusEmoji =
                {
                    args.Player.PlayerState == PlayerState.Playing
                        ? new Emoji("\u25B6") // Playing
                        : new Emoji("\u23F8"), // Paused

                    service.Repeat switch
                    {
                        RepeatMode.Once => new Emoji("\uD83D\uDD02"),
                        RepeatMode.First => new Emoji("\uD83D\uDD01"),
                        RepeatMode.Queue => new Emoji("\uD83D\uDD01"),

                        _ => null
                    }
                };

                await args.Player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{nextTrack.Title}")
                    .WithUrl(nextTrack.Url)
                    .WithThumbnailUrl(nextTrack.ThumbnailUrl)
                    .WithFields(
                        new EmbedFieldBuilder
                        {
                            Name = "Autor:",
                            Value = nextTrack.Author,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Délka:",
                            Value = $"`{nextTrack.Duration}`",
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Vyžádal:",
                            Value = nextTrack.RequestedBy.Mention,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Hlasitost:",
                            Value = $"{args.Player.Volume}%",
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Stav:",
                            Value = $"{string.Join(' ', statusEmoji)}",
                            IsInline = true
                        }
                    ).Build());
            }

            else
            {
                await args.Player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Stream audia byl úspěšně dokončen").Build());
            }

            service.Timer.Reset();
        }
    }
}
