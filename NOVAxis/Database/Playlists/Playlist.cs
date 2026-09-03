using System;
using System.Collections.Generic;

namespace NOVAxis.Database.Playlists
{
    /// <summary>
    /// A named list of tracks belonging to one person. A playlist with a
    /// <see cref="GuildId"/> is also offered to everyone in that guild, which is the only
    /// way one becomes visible to anybody else.
    /// </summary>
    public class Playlist
    {
        public ulong Id { get; set; }
        public ulong OwnerId { get; set; }

        /// <summary>The guild it is shared with, or null while it is only the owner's.</summary>
        public ulong? GuildId { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// Who the owner was called when it was saved. Kept because a playlist offered to a
        /// guild has to name somebody, and the bot cannot always reach a user it no longer
        /// shares a guild with.
        /// </summary>
        public string OwnerName { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public List<PlaylistTrack> Tracks { get; set; } = new();
    }
}
