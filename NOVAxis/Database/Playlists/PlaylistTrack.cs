using System;

using NOVAxis.Services.Audio;

namespace NOVAxis.Database.Playlists
{
    /// <summary>
    /// One saved track. Everything the player and the queue draw is stored, rather than
    /// the link alone: loading a playlist of forty links would otherwise be forty
    /// extractor runs before a single row could be shown. The stream URL is deliberately
    /// not among them - it is short lived and the player resolves it when the track is
    /// actually played, so what is stored here cannot go stale.
    /// </summary>
    public class PlaylistTrack
    {
        public ulong Id { get; set; }
        public ulong PlaylistId { get; set; }
        public Playlist Playlist { get; set; }

        /// <summary>Position in the playlist, from zero.</summary>
        public int Position { get; set; }

        public string Title { get; set; }
        public string Author { get; set; }
        public string Url { get; set; }
        public string ArtworkUrl { get; set; }
        public long DurationMs { get; set; }
        public string Identifier { get; set; }
        public string SourceName { get; set; }

        public static PlaylistTrack FromTrack(AudioTrack track, int position)
        {
            return new PlaylistTrack
            {
                Position = position,
                Title = track.Title,
                Author = track.Author,
                Url = track.Uri?.AbsoluteUri,
                ArtworkUrl = track.ArtworkUri?.AbsoluteUri,
                DurationMs = (long)track.Duration.TotalMilliseconds,
                Identifier = track.Identifier,
                SourceName = track.SourceName
            };
        }

        public AudioTrack ToTrack()
        {
            return new AudioTrack
            {
                Title = Title,
                Author = Author,
                Uri = Read(Url),
                ArtworkUri = Read(ArtworkUrl),
                Duration = TimeSpan.FromMilliseconds(DurationMs),
                Identifier = Identifier,
                SourceName = SourceName
            };
        }

        private static Uri Read(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null;
        }
    }
}
