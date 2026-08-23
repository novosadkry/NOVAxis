using System.Collections.Generic;

using Lavalink4NET.Players;
using Lavalink4NET.Tracks;

namespace NOVAxis.Services.Audio.Lavalink
{
    /// <summary>
    /// An <see cref="AudioTrack"/> which keeps hold of the Lavalink track it was built from,
    /// so that it can be handed back to the node without a round trip through the encoder.
    /// </summary>
    public record LavalinkAudioTrack : AudioTrack
    {
        public LavalinkTrack Inner { get; init; }

        public static LavalinkAudioTrack FromLavalink(LavalinkTrack track)
        {
            return new LavalinkAudioTrack
            {
                Title = track.Title,
                Author = track.Author,
                Uri = track.Uri,
                ArtworkUri = track.ArtworkUri,
                Duration = track.Duration,
                IsLiveStream = track.IsLiveStream,
                Identifier = track.Identifier,
                SourceName = track.SourceName,
                Inner = track
            };
        }
    }

    /// <summary>
    /// Bridges our queue items into the shape Lavalink4NET expects.
    /// </summary>
    internal sealed class LavalinkTrackQueueItem : ITrackQueueItem
    {
        public AudioTrackQueueItem Item { get; }
        public TrackReference Reference { get; }

        public LavalinkTrack Track => Reference.Track;

        public LavalinkTrackQueueItem(AudioTrackQueueItem item)
        {
            Item = item;
            Reference = new TrackReference(((LavalinkAudioTrack)item.Track).Inner);
        }

        public override bool Equals(object obj)
        {
            return obj is LavalinkTrackQueueItem other &&
                   Item.RequestId.Equals(other.Item.RequestId);
        }

        public override int GetHashCode()
        {
            return Item.RequestId.GetHashCode();
        }

        public static AudioTrackQueueItem Unwrap(ITrackQueueItem item)
        {
            return item switch
            {
                null => null,
                LavalinkTrackQueueItem wrapper => wrapper.Item,

                // Tracks queued by Lavalink itself (autoplay) carry no request context
                _ => new AudioTrackQueueItem
                {
                    Track = LavalinkAudioTrack.FromLavalink(item.Track)
                }
            };
        }

        public static IEnumerable<ITrackQueueItem> Wrap(IEnumerable<AudioTrackQueueItem> items)
        {
            foreach (var item in items)
                yield return new LavalinkTrackQueueItem(item);
        }
    }
}
