using System.Collections.Generic;

namespace NOVAxis.Database.Guild
{
    public class GuildInfo
    {
        public ulong Id { get; set; }
        public string Prefix { get; set; }
        public List<GuildRole> Roles { get; set; } = new();
    }
}