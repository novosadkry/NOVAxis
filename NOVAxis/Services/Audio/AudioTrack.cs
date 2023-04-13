using NOVAxis.Extensions;

using Discord;
using Victoria.Player;

namespace NOVAxis.Services.Audio
{
    public class AudioTrack : LavaTrack
    {
        public AudioTrack(LavaTrack lavaTrack)
            : base(lavaTrack) { }

        public ulong RequestId { get; init; }
        public IUser RequestedBy { get; init; }
        public string ThumbnailUrl => this.GetThumbnailUrl();

        public override bool Equals(object obj)
        {
            return obj is AudioTrack other && 
                   RequestId.Equals(other.RequestId);
        }

        public override int GetHashCode()
        {
            return RequestId.GetHashCode();
        }
    }
}