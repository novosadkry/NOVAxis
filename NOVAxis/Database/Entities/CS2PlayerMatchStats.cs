namespace NOVAxis.Database.Entities
{
    public class CS2PlayerMatchStats
    {
        public int Id { get; set; }
        public int PlayerId { get; set; }
        public int MatchId { get; set; }

        public int Kills { get; set; }
        public int Deaths { get; set; }
        public int Assists { get; set; }
        public int HeadshotKills { get; set; }
        public int Score { get; set; }
        public int MVPs { get; set; }

        public string Team { get; set; }

        public CS2Player Player { get; set; }
        public CS2Match Match { get; set; }
    }
}
