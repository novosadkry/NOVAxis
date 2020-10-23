using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

using NOVAxis.Extensions;

using Discord;
using SharpLink;

namespace NOVAxis.Services
{
    public class AudioModuleService
    {
        public class Context
        {
            public Context(ulong id)
            {
                GuildId = id;
            }

            public class ContextTrack
            {
                public LavalinkTrack Value { get; set; }
                public IUser RequestedBy { get; set; }
                public string ThumbnailUrl => Value.GetThumbnailUrl();

                public static explicit operator LavalinkTrack(ContextTrack track)
                {    
                    return track.Value;
                }
            }

            public class ContextTimer
            {
                private Timer _timer;
                public bool IsSet { get; private set; }
                public bool Elapsed { get; private set; }

                public void Set(double interval, ElapsedEventHandler elapsedEvent)
                {
                    _timer = new Timer(interval);
                    _timer.Elapsed += (sender, e) => Elapsed = true;
                    _timer.Elapsed += elapsedEvent;

                    IsSet = true;
                }

                public void Reset()
                {
                    _timer.Stop();
                    _timer.Start();

                    Elapsed = false;
                }

                public void Start() => _timer.Start();
                public void Stop() => _timer.Stop();

                public void Dispose() 
                { 
                    _timer.Dispose(); 
                    IsSet = false; 
                    Elapsed = false; 
                }
            }

            public LavalinkPlayer GetPlayer() => LavalinkService.Manager.GetPlayer(GuildId);

            public List<ContextTrack> Queue { get; set; } = new List<ContextTrack>();

            public ContextTimer Timer { get; set; } = new ContextTimer();

            public ContextTrack CurrentTrack => Queue.First();
            public ContextTrack LastTrack => Queue.Last();

            public uint Volume { get; set; } = 100;
            public ulong GuildId { get; }

            public IMessageChannel BoundChannel { get; set; }
        }

        public AudioModuleService()
        {
            AudioTimeout = Program.Config.AudioTimeout;
            guilds = new ConcurrentDictionary<ulong, Context>();

            LavalinkService.Manager.TrackEnd -= AudioModuleService_TrackEnd;
            LavalinkService.Manager.TrackEnd += AudioModuleService_TrackEnd;
        }

        public long AudioTimeout { get; }

        private ConcurrentDictionary<ulong, Context> guilds;

        public Context this[ulong id]
        {
            get => guilds.GetOrAdd(id, new Context(id));
            set => guilds[id] = value;
        }

        private async Task AudioModuleService_TrackEnd(LavalinkPlayer player, LavalinkTrack track, string _)
        {
            var service = guilds[player.VoiceChannel.GuildId];

            if (service.Queue.Count == 0)
                return;

            service.Queue.RemoveAt(0);

            if (service.Queue.Count > 0)
            {
                Context.ContextTrack nextTrack = service.Queue.First();

                await player.PlayAsync(nextTrack.Value);

                await service.BoundChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{new Emoji("\u25B6")} {service.LastTrack.Value.Title}")
                    .WithUrl(nextTrack.Value.Url)
                    .WithThumbnailUrl(nextTrack.ThumbnailUrl)
                    .WithFields(
                        new EmbedFieldBuilder
                        {
                            Name = "Autor:",
                            Value = nextTrack.Value.Author,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Délka:",
                            Value = $"`{nextTrack.Value.Length}`",
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
                            Value = $"{service.Volume}%",
                            IsInline = true
                        }
                    ).Build());
            }

            else
            {
                await service.BoundChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Stream audia byl úspěšně dokončen").Build());
            }

            service.Timer.Reset();
        }
    }
}
