using System.Threading.Tasks;

using Discord;
using Discord.Commands;

namespace NOVAxis.Services.Guild
{
    public class GuildService
    {
        public GuildDbContext DbContext { get; }

        public GuildService(GuildDbContext dbContext)
        {
            DbContext = dbContext;
            DbContext.Database.EnsureCreated();
        }

        public async Task<GuildInfo> GetInfo(ICommandContext context)
        {
            if (context.User is IGuildUser)
                return await GetInfo(context.Guild.Id);

            return GuildInfo.Default;
        }

        public async Task<GuildInfo> GetInfo(ulong id)
        {
            return await DbContext.Guilds.FindAsync(id) 
                   ?? GuildInfo.Default;
        }

        public async Task SetInfo(GuildInfo info)
        {
            await DbContext.Guilds.AddAsync(info);
            await DbContext.SaveChangesAsync();
        }
    }
}
