using System;
using Microsoft.EntityFrameworkCore;
using NOVAxis.Core;

namespace NOVAxis.Services.Guild
{
    public class GuildDbContext : DbContext
    {
        public DbSet<GuildInfo> Guilds { get; set; }

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
                    options.UseMySQL(ConnectionString);
                    break;

                case "sqlite":
                    options.UseSqlite(ConnectionString);
                    break;
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GuildInfo>()
                .HasKey(x => x.GuildId);
        }
    }
}
