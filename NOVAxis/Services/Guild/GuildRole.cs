using System.ComponentModel.DataAnnotations.Schema;

namespace NOVAxis.Services.Guild
{
    [Table("GuildRoles")]
    public class GuildRole
    {
        public GuildInfo Guild { get; set; }

        public ulong Id { get; set; }
        public string Name { get; set; }
    }
}
