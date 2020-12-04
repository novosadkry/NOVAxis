using System.Linq;

namespace NOVAxis.Services.Audio
{
    public enum RepeatMode
    {
        None,
        Once,
        First,
        Queue
    }

    public class AudioContext
    {
        public AudioContext(ulong id)
        {
            GuildId = id;
        }

        public AudioTimer Timer { get; set; } = new AudioTimer();
        public LinkedQueue<AudioTrack> Queue { get; set; } = new LinkedQueue<AudioTrack>();

        public AudioTrack Track => Queue.First();
        public AudioTrack LastTrack => Queue.Last();

        public RepeatMode Repeat { get; set; }
        public ulong GuildId { get; }
    }
}