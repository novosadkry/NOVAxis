using System.Collections.Generic;

using NOVAxis.Core;

namespace NOVAxis.Services.Guild
{
    public class GuildInfo
    {
        public ulong Id { get; set; }
        public string Prefix { get; set; }
        public List<GuildRole> Roles { get; set; }

        public static GuildInfo Default(ProgramConfig config)
        {
            return new GuildInfo
            {
                Prefix = config.Interaction.DefaultPrefix
            };
        }
    }
}