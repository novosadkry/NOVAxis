using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace NOVAxis.Services.Audio
{
    /// <summary>
    /// A single playable audio track, independent of the backend which produced it.
    /// </summary>
    public record AudioTrack
    {
        public string Title { get; init; }
        public string Author { get; init; }
        public Uri Uri { get; init; }
        public Uri ArtworkUri { get; init; }
        public TimeSpan Duration { get; init; }
        public bool IsLiveStream { get; init; }

        /// <summary>
        /// Backend specific identifier. For yt-dlp this is the extractor's video id,
        /// for Lavalink it is the encoded track.
        /// </summary>
        public string Identifier { get; init; }

        public string SourceName { get; init; }
    }

    public record AudioPlaylist
    {
        public string Name { get; init; }
        public Uri Uri { get; init; }
        public Uri ArtworkUri { get; init; }
        public int? TotalTracks { get; init; }
    }

    /// <summary>
    /// The outcome of a search or an URL lookup.
    /// </summary>
    public class AudioLoadResult
    {
        public static AudioLoadResult Failed { get; } = new(ImmutableArray<AudioTrack>.Empty, null);

        public ImmutableArray<AudioTrack> Tracks { get; }
        public AudioPlaylist Playlist { get; }

        public AudioTrack Track => Tracks.IsDefaultOrEmpty ? null : Tracks[0];
        public bool IsPlaylist => Playlist != null;
        public bool IsFailed => Tracks.IsDefaultOrEmpty;

        private AudioLoadResult(ImmutableArray<AudioTrack> tracks, AudioPlaylist playlist)
        {
            Tracks = tracks;
            Playlist = playlist;
        }

        public static AudioLoadResult FromTrack(AudioTrack track)
        {
            return track == null
                ? Failed
                : new AudioLoadResult(ImmutableArray.Create(track), null);
        }

        public static AudioLoadResult FromPlaylist(IEnumerable<AudioTrack> tracks, AudioPlaylist playlist)
        {
            var immutable = tracks.ToImmutableArray();

            return immutable.IsEmpty
                ? Failed
                : new AudioLoadResult(immutable, playlist);
        }
    }
}
