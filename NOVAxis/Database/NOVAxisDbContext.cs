using System;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

using NOVAxis.Core;
using NOVAxis.Database.Guild;
using NOVAxis.Database.Playlists;

namespace NOVAxis.Database
{
    public class NOVAxisDbContext : DbContext
    {
        public virtual DbSet<GuildInfo> Guilds { get; set; }
        public virtual DbSet<GuildRole> GuildRoles { get; set; }
        public virtual DbSet<Playlist> Playlists { get; set; }
        public virtual DbSet<PlaylistTrack> PlaylistTracks { get; set; }

        private DatabaseOptions Options { get; }

        public NOVAxisDbContext(IOptions<DatabaseOptions> options)
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

            modelBuilder.Entity<Playlist>(playlist =>
            {
                playlist.Property(x => x.Id).ValueGeneratedNever();

                playlist
                    .HasMany(x => x.Tracks)
                    .WithOne(x => x.Playlist)
                    .HasForeignKey(x => x.PlaylistId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Every lookup is either "mine" or "this guild's", so both are indexed
                playlist.HasIndex(x => x.OwnerId);
                playlist.HasIndex(x => x.GuildId);
            });

            modelBuilder.Entity<PlaylistTrack>(track =>
            {
                track.Property(x => x.Id).ValueGeneratedNever();
                track.HasIndex(x => new { x.PlaylistId, x.Position });
            });
        }
    }
}
