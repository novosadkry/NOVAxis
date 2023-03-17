using System.Threading.Tasks;

using NOVAxis.Core;

using Discord;
using Discord.Commands;

namespace NOVAxis.Services.Guild
{
    public class GuildService
    {
        private GuildDbContext DbContext { get; }
        private ProgramConfig Config { get; }

        public GuildService(ProgramConfig config, GuildDbContext dbContext)
        {
            Config = config;
            DbContext = dbContext;
            DbContext.Database.EnsureCreated();
        }

        public async Task<GuildInfo> GetInfo(ICommandContext context)
        {
            if (context.User is IGuildUser)
                return await GetInfo(context.Guild.Id);

            return GuildInfo.Default(Config);
        }

        public async Task<GuildInfo> GetInfo(ulong id)
        {
            return await DbContext.Guilds.FindAsync(id) 
                   ?? GuildInfo.Default(Config);
        }

        public async Task SetInfo(GuildInfo info)
        {
            await DbContext.Guilds.AddAsync(info);
            await DbContext.SaveChangesAsync();
        }
    }
}
