using System;
using System.Collections.Generic;

namespace NOVAxis.Database.Entities
{
    public class CS2Match
    {
        public int Id { get; set; }
        public string DemoUrl { get; set; }
        public string MapName { get; set; }
        public DateTime MatchDate { get; set; }
        public int DurationSeconds { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;

        public ICollection<CS2PlayerMatchStats> PlayerStats { get; set; }
    }
}
