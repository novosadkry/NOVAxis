using System;
using Microsoft.EntityFrameworkCore;
using NOVAxis.Core;

namespace NOVAxis.Services.Guild
{
    public class GuildDbContext : DbContext
    {
        public DbSet<GuildInfo> Guilds { get; set; }
        public ProgramConfig.DatabaseObject Config { get; }

        public GuildDbContext()
        {
            Config = Program.Config.Database;
        }

        private string ConnectionString => Config.DbType switch
        {
            "mysql" => $"Server={Config.DbHost};" +
                       $"Port={Config.DbPort};" +
                       $"Database={Config.DbName};" +
                       $"Uid={Config.DbUsername};" +
                       $"Pwd={Config.DbPassword}",

            "sqlite" => $"Data Source={Config.DbName}.db",

            _ => throw new InvalidOperationException("Invalid DbType supplied")
        };

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            if (!Config.Active)
            {
                options.UseInMemoryDatabase("novaxis");
                return;
            }

            switch (Config.DbType)
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
