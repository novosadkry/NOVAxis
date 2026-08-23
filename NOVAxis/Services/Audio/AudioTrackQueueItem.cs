using System.Threading;

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
        private static ulong _lastRequestId;

        public AudioTrack Track { get; init; }
        public ulong RequestId { get; init; }
        public IUser RequestedBy { get; init; }

        /// <summary>
        /// Hands out an id no other item shares. A timestamp is not enough, since a whole
        /// playlist is turned into items within a single tick.
        /// </summary>
        public static ulong NextRequestId()
        {
            return Interlocked.Increment(ref _lastRequestId);
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
