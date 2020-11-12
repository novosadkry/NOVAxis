using System.Collections.Concurrent;
using System.Threading.Tasks;

using NOVAxis.Core;
using Discord;

using Victoria;
using Victoria.EventArgs;

namespace NOVAxis.Services.Audio
{
    public class AudioModuleService
    {
        public long AudioTimeout { get; }

        private readonly ConcurrentDictionary<ulong, AudioContext> _guilds;

        public AudioModuleService(LavaNode lavaNodeInstance)
        {
            AudioTimeout = Program.Config.AudioTimeout;
            _guilds = new ConcurrentDictionary<ulong, AudioContext>();

            lavaNodeInstance.OnTrackEnded -= AudioModuleService_TrackEnd;
            lavaNodeInstance.OnTrackEnded += AudioModuleService_TrackEnd;
        }

        public AudioContext this[ulong id]
        {
            get => _guilds.GetOrAdd(id, new AudioContext(id));
            set => _guilds[id] = value;
        }

        private async Task AudioModuleService_TrackEnd(TrackEndedEventArgs args)
        {
            if (args.Player.VoiceChannel == null)
                return;

            var service = _guilds[args.Player.VoiceChannel.GuildId];

            if (service.Queue.Count == 0)
                return;

            service.Queue.Dequeue();

            if (service.Queue.Count > 0)
            {
                AudioContext.ContextTrack nextTrack = service.Queue.Peek();

                await args.Player.PlayAsync(nextTrack);

                await args.Player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{new Emoji("\u25B6")} {nextTrack.Title}")
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
