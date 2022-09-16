using NOVAxis.Core;

namespace NOVAxis.Services.Guild
{
    public struct GuildInfo
    {
        public string Prefix { get; set; }
        public ulong DjRole { get; set; }

        public static GuildInfo Default => 
            new() { Prefix = Program.Config.Interaction.DefaultPrefix };
    }
}