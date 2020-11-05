using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;

using NOVAxis.Core;
using NOVAxis.Extensions;

using Discord;
using Victoria;
using Victoria.EventArgs;

namespace NOVAxis.Services
{
    public class AudioModuleService
    {
        public class LinkedQueue<T> : LinkedList<T>
        {
            public void Enqueue(T value)
            {
                AddLast(value);
            }

            public void Enqueue(IEnumerable<T> values)
            {
                foreach (T value in values)
                    AddLast(value);
            }

            public T Dequeue()
            {
                T value = this.First();
                RemoveFirst();

                return value;
            }

            public T Peek()
            {
                return this.First();
            }
        }

        public class Context
        {
            public Context(ulong id)
            {
                GuildId = id;
            }

            public class ContextTrack
            {
                public LavaTrack Value { get; set; }
                public IUser RequestedBy { get; set; }
                public string ThumbnailUrl => Value.GetThumbnailUrl(); 

                public static implicit operator LavaTrack(ContextTrack track)
                {    
                    return track.Value;
                }
            }

            public class ContextTimer : IDisposable
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
                    Stop(); Start();
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

            public LinkedQueue<ContextTrack> Queue { get; set; } = new LinkedQueue<ContextTrack>();
            public ContextTrack Track => Queue.First();
            public ContextTrack LastTrack => Queue.Last();

            public ContextTimer Timer { get; set; } = new ContextTimer();

            public ulong GuildId { get; }
        }

        public AudioModuleService(LavaNode lavaNodeInstance)
        {
            AudioTimeout = Program.Config.AudioTimeout;
            _guilds = new ConcurrentDictionary<ulong, Context>();

            lavaNodeInstance.OnTrackEnded -= AudioModuleService_TrackEnd;
            lavaNodeInstance.OnTrackEnded += AudioModuleService_TrackEnd;
        }

        public long AudioTimeout { get; }

        private readonly ConcurrentDictionary<ulong, Context> _guilds;

        public Context this[ulong id]
        {
            get => _guilds.GetOrAdd(id, new Context(id));
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
                Context.ContextTrack nextTrack = service.Queue.Peek();

                await args.Player.PlayAsync(nextTrack.Value);

                await args.Player.TextChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{new Emoji("\u25B6")} {nextTrack.Value.Title}")
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
                            Value = $"`{nextTrack.Value.Duration}`",
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
