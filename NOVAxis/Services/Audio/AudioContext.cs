using System;
using System.Linq;
using System.Timers;

using NOVAxis.Extensions;

using Discord;
using Victoria;

namespace NOVAxis.Services.Audio
{
    public class AudioContext
    {
        public AudioContext(ulong id)
        {
            GuildId = id;
        }

        public class ContextTrack : LavaTrack
        {
            public ContextTrack(LavaTrack lavaTrack)
                : base(lavaTrack) { }

            public IUser RequestedBy { get; set; }
            public string ThumbnailUrl => this.GetThumbnailUrl();
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
}