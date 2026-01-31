using System;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Database.Entities;

namespace NOVAxis.Database
{
    public class ProgramDbContext : DbContext
    {
        public virtual DbSet<GuildInfo> Guilds { get; set; }
        public virtual DbSet<GuildRole> GuildRoles { get; set; }
        public virtual DbSet<DownloadInfo> Downloads { get; set; }
        public virtual DbSet<CS2Player> CS2Players { get; set; }
        public virtual DbSet<CS2Match> CS2Matches { get; set; }
        public virtual DbSet<CS2DemoQueue> CS2DemoQueue { get; set; }
        public virtual DbSet<CS2PlayerMatchStats> CS2PlayerMatchStats { get; set; }

        private DatabaseOptions Options { get; }

        public ProgramDbContext(IOptions<DatabaseOptions> options)
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
            modelBuilder.Entity<GuildInfo>(entity =>
            {
                entity.Property(x => x.Id)
                    .ValueGeneratedNever();

                entity.HasMany(x => x.Roles)
                    .WithOne(x => x.Guild);
            });

            modelBuilder.Entity<GuildRole>()
                .Property(x => x.Id)
                .ValueGeneratedNever();

            modelBuilder.Entity<DownloadInfo>()
                .HasKey(x => x.Uuid);

            modelBuilder.Entity<CS2DemoQueue>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.DemoUrl).IsRequired();
            });

            modelBuilder.Entity<CS2Player>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.SteamId).IsUnique();
                entity.Property(e => e.SteamId).IsRequired();
                entity.Property(e => e.Name).IsRequired();
            });

            modelBuilder.Entity<CS2Match>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.DemoUrl).IsUnique();
                entity.Property(e => e.DemoUrl).IsRequired();
                entity.Property(e => e.MapName).IsRequired();
            });

            modelBuilder.Entity<CS2PlayerMatchStats>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(e => e.Player)
                    .WithMany(p => p.GameStats)
                    .HasForeignKey(e => e.PlayerId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Match)
                    .WithMany(g => g.PlayerStats)
                    .HasForeignKey(e => e.MatchId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.Property(e => e.Team).IsRequired();
            });
        }
    }
}
