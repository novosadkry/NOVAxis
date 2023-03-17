using NOVAxis.Core;

namespace NOVAxis.Services.Guild
{
    public class GuildInfo
    {
        public ulong GuildId { get; set; }
        public string Prefix { get; set; }
        public ulong DjRole { get; set; }

        public static GuildInfo Default(ProgramConfig config)
        {
            return new GuildInfo
            {
                Prefix = config.Interaction.DefaultPrefix
            };
        }
    }
}