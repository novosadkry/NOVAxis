using System;
using System.Linq;

using NOVAxis.Utilities;

namespace NOVAxis.Services.Audio
{
    public enum RepeatMode
    {
        None,
        Once,
        First,
        Queue
    }

    public class AudioContext : IDisposable
    {
        public AudioContext(ulong id)
        {
            GuildId = id;
        }

        private bool _disposed;
        private readonly object _disposeLock = new object();

        public Timer Timer { get; set; } = new Timer();
        public LinkedQueue<AudioTrack> Queue { get; set; } = new LinkedQueue<AudioTrack>();

        public AudioTrack Track => Queue.First();
        public AudioTrack LastTrack => Queue.Last();

        public RepeatMode Repeat { get; set; }
        public ulong GuildId { get; }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed)
                    return;

                Queue.Clear();
                Timer.Dispose();
                Repeat = RepeatMode.None;

                _disposed = true;
            }
        }

        ~AudioContext()
        {
            Dispose();
        }
    }
}