using System;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

using NOVAxis.Core;

namespace NOVAxis.Database.Guild
{
    public class GuildDbContext : DbContext
    {
        public virtual DbSet<GuildInfo> Guilds { get; set; }
        public virtual DbSet<GuildRole> GuildRoles { get; set; }

        private DatabaseOptions Options { get; }

        public GuildDbContext(IOptions<DatabaseOptions> options)
        {
            Options = options.Value;
        }

        private string ConnectionString => Options.DbType switch
        {
            "mysql" => $"Server={Options.DbHost};" +
                       $"Port={Options.DbPort};" +
                       $"Database={Options.DbName};" +
                       $"Uid={Options.DbUsername};" +
                       $"Pwd={Options.DbPassword}",

            "sqlite" => $"Data Source={Options.DbName}.db",

            _ => throw new InvalidOperationException("Invalid DbType supplied")
        };

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            if (!Options.Active)
            {
                options.UseInMemoryDatabase("novaxis");
                return;
            }

            switch (Options.DbType)
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
    }
}
