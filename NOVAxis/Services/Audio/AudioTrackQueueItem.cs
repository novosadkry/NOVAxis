using Discord;

namespace NOVAxis.Services.Audio
{
    /// <summary>
    /// A queue entry pairing a track with the user who asked for it.
    /// <see cref="RequestId"/> gives every entry a stable identity, so that duplicates
    /// of the same track stay distinguishable inside the queue and inside interactions.
    /// </summary>
    public class AudioTrackQueueItem
    {
        public AudioTrack Track { get; init; }
        public ulong RequestId { get; init; }
        public IUser RequestedBy { get; init; }

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
