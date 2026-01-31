using System;
using System.Collections.Generic;

namespace NOVAxis.Database.Entities
{
    public class CS2Player
    {
        public int Id { get; set; }
        public string SteamId { get; set; }
        public ulong? DiscordId { get; set; }
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<CS2PlayerMatchStats> GameStats { get; set; }
    }
}
