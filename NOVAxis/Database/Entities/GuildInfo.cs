using System.Collections.Generic;

namespace NOVAxis.Database.Entities
{
    public class GuildInfo
    {
        public ulong Id { get; set; }
        public string Prefix { get; set; }

        public virtual List<GuildRole> Roles { get; set; }
    }
}
