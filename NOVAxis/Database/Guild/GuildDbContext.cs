using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

using Discord;
using Discord.Commands;

using NOVAxis.Core;

namespace NOVAxis.Database.Guild
{
    public class GuildDbContext : DbContext
    {
        public virtual DbSet<GuildInfo> Guilds { get; set; }
        public virtual DbSet<GuildRole> GuildRoles { get; set; }

        private ProgramConfig Config { get; }

        public GuildDbContext(ProgramConfig config)
        {
            Config = config;
        }

        private string ConnectionString => Config.Database.DbType switch
        {
            "mysql" => $"Server={Config.Database.DbHost};" +
                       $"Port={Config.Database.DbPort};" +
                       $"Database={Config.Database.DbName};" +
                       $"Uid={Config.Database.DbUsername};" +
                       $"Pwd={Config.Database.DbPassword}",

            "sqlite" => $"Data Source={Config.Database.DbName}.db",

            _ => throw new InvalidOperationException("Invalid DbType supplied")
        };

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            if (!Config.Database.Active)
            {
                options.UseInMemoryDatabase("novaxis");
                return;
            }

            switch (Config.Database.DbType)
            {
                case "mysql":
                    var serverVersion = ServerVersion.AutoDetect(ConnectionString);
                    options.UseMySql(ConnectionString, serverVersion);
                    break;

                case "sqlite":
                    options.UseSqlite(ConnectionString);
                    break;
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GuildInfo>()
                .HasMany(x => x.Roles)
                .WithOne(x => x.Guild);

            modelBuilder.Entity<GuildInfo>()
                .Property(x => x.Id)
                .ValueGeneratedNever();

            modelBuilder.Entity<GuildRole>()
                .Property(x => x.Id)
                .ValueGeneratedNever();
        }

        public async Task<GuildInfo> Get(ICommandContext context)
        {
            if (context.User is IGuildUser)
                return await Get(context.Guild);

            return null;
        }

        public async Task<GuildInfo> Get(IInteractionContext context)
        {
            if (context.User is IGuildUser)
                return await Get(context.Guild);

            return null;
        }

        public async Task<GuildInfo> Get(IGuild guild)
        {
            return await Guilds
                .Include(x => x.Roles)
                .SingleOrDefaultAsync(x => x.Id == guild.Id);
        }

        public async Task<GuildInfo> Create(IGuild guild)
        {
            var guildInfo = new GuildInfo
            {
                Id = guild.Id,
                Prefix = Config.Interaction.DefaultPrefix
            };

            Guilds.Add(guildInfo);
            await SaveChangesAsync();

            return guildInfo;
        }
    }
}
