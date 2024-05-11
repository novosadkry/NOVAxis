using Discord;
using Lavalink4NET.Tracks;
using Lavalink4NET.Players;

namespace NOVAxis.Services.Audio
{
    public class AudioTrackQueueItem : ITrackQueueItem
    {
        public TrackReference Reference { get; }
        public ulong RequestId { get; init; }
        public IUser RequestedBy { get; init; }

        public LavalinkTrack Track => Reference.Track;

        public AudioTrackQueueItem(TrackReference reference)
        {
            Reference = reference;
        }

        public override bool Equals(object obj)
        {
            return obj is AudioTrackQueueItem other &&
                   RequestId.Equals(other.RequestId);
        }

        public override int GetHashCode()
        {
            return RequestId.GetHashCode();
        }
    }
}
