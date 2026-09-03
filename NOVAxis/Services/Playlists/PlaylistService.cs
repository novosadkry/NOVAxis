using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Database;
using NOVAxis.Database.Playlists;
using NOVAxis.Services.Audio;
using NOVAxis.Utilities;

namespace NOVAxis.Services.Playlists
{
    public enum PlaylistFailure
    {
        Disabled,
        NoName,
        NameTooLong,
        Empty,
        TooMany,
        TooLong,
        NotFound,
        NotYours
    }

    public class PlaylistException : Exception
    {
        public PlaylistFailure Failure { get; }

        public PlaylistException(PlaylistFailure failure, string message) : base(message)
        {
            Failure = failure;
        }
    }

    /// <summary>
    /// Saved playlists, over the one database. A singleton reaching a scoped context
    /// through the scope factory, the way the rest of the bot's long lived services do -
    /// nothing here holds a context between calls.
    /// </summary>
    public class PlaylistService
    {
        private IServiceScopeFactory Scopes { get; }
        private IOptions<PlaylistOptions> Options { get; }

        public PlaylistService(IServiceScopeFactory scopes, IOptions<PlaylistOptions> options)
        {
            Scopes = scopes;
            Options = options;
        }

        public bool Active => Options.Value.Active;

        /// <summary>
        /// Everything the caller may open in this guild: their own, plus whatever anyone
        /// has shared with the guild they are asking from.
        /// </summary>
        public async Task<IReadOnlyList<Playlist>> ListAsync(
            ulong ownerId, ulong? guildId, CancellationToken cancellationToken = default)
        {
            Require();

            await using var scope = Scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NOVAxisDbContext>();

            return await Visible(db, ownerId, guildId)
                .OrderBy(x => x.Name)
                .ToListAsync(cancellationToken);
        }

        /// <summary>The playlist with its tracks, or null where the caller may not see it.</summary>
        public async Task<Playlist> GetAsync(
            ulong id, ulong ownerId, ulong? guildId, CancellationToken cancellationToken = default)
        {
            Require();

            await using var scope = Scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NOVAxisDbContext>();

            var playlist = await Visible(db, ownerId, guildId)
                .Include(x => x.Tracks)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            Sort(playlist);

            return playlist;
        }

        /// <summary>
        /// The same, by name rather than by id - what a slash command has to work with.
        /// A name the caller owns wins over one merely shared with the guild.
        /// </summary>
        public async Task<Playlist> FindAsync(
            string name, ulong ownerId, ulong? guildId, CancellationToken cancellationToken = default)
        {
            Require();

            await using var scope = Scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NOVAxisDbContext>();

            var matches = await Visible(db, ownerId, guildId)
                .Include(x => x.Tracks)
                .Where(x => x.Name == name)
                .ToListAsync(cancellationToken);

            var playlist = matches.FirstOrDefault(x => x.OwnerId == ownerId) ?? matches.FirstOrDefault();

            Sort(playlist);

            return playlist;
        }

        /// <summary>
        /// Stores <paramref name="tracks"/> under <paramref name="name"/>, replacing a
        /// playlist of the caller's own by that name rather than making a second one.
        /// </summary>
        public async Task<Playlist> SaveAsync(
            ulong ownerId,
            string ownerName,
            ulong? guildId,
            string name,
            IReadOnlyList<AudioTrack> tracks,
            CancellationToken cancellationToken = default)
        {
            Require();

            var options = Options.Value;

            name = name?.Trim();

            if (string.IsNullOrEmpty(name))
                throw new PlaylistException(PlaylistFailure.NoName, "Playlist potřebuje jméno");

            if (name.Length > options.MaxNameLength)
                throw new PlaylistException(PlaylistFailure.NameTooLong,
                    $"Jméno smí mít nejvýš {options.MaxNameLength} znaků");

            if (tracks == null || tracks.Count == 0)
                throw new PlaylistException(PlaylistFailure.Empty, "Není co uložit");

            if (tracks.Count > options.MaxTracks)
                throw new PlaylistException(PlaylistFailure.TooLong,
                    $"Playlist pojme nejvýš {options.MaxTracks} skladeb");

            await using var scope = Scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NOVAxisDbContext>();

            var existing = await db.Playlists
                .Include(x => x.Tracks)
                .FirstOrDefaultAsync(x => x.OwnerId == ownerId && x.Name == name, cancellationToken);

            if (existing == null)
            {
                var held = await db.Playlists.CountAsync(x => x.OwnerId == ownerId, cancellationToken);

                if (held >= options.MaxPerUser)
                    throw new PlaylistException(PlaylistFailure.TooMany,
                        $"Máš uložených {held} playlistů, víc než {options.MaxPerUser} jich mít nemůžeš");

                existing = new Playlist
                {
                    Id = Snowflake.Next(),
                    OwnerId = ownerId,
                    Name = name,
                    CreatedAt = DateTime.UtcNow
                };

                db.Playlists.Add(existing);
            }
            else
            {
                db.PlaylistTracks.RemoveRange(existing.Tracks);
                existing.Tracks.Clear();
            }

            existing.OwnerName = ownerName;
            existing.GuildId = guildId;
            existing.UpdatedAt = DateTime.UtcNow;

            for (var i = 0; i < tracks.Count; i++)
            {
                var track = PlaylistTrack.FromTrack(tracks[i], i);
                track.Id = Snowflake.Next();
                track.PlaylistId = existing.Id;

                existing.Tracks.Add(track);
            }

            await db.SaveChangesAsync(cancellationToken);

            return existing;
        }

        /// <summary>Only the owner may throw one away, sharing or not.</summary>
        public async Task<Playlist> DeleteAsync(
            ulong id, ulong ownerId, CancellationToken cancellationToken = default)
        {
            Require();

            await using var scope = Scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NOVAxisDbContext>();

            var playlist = await db.Playlists
                .Include(x => x.Tracks)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (playlist == null)
                throw new PlaylistException(PlaylistFailure.NotFound, "Takový playlist neznám");

            if (playlist.OwnerId != ownerId)
                throw new PlaylistException(PlaylistFailure.NotYours, "Smazat ho může jen jeho autor");

            db.Playlists.Remove(playlist);
            await db.SaveChangesAsync(cancellationToken);

            return playlist;
        }

        /// <summary>Shares the playlist with a guild, or takes it back to being private.</summary>
        public async Task<Playlist> ShareAsync(
            ulong id, ulong ownerId, ulong? guildId, CancellationToken cancellationToken = default)
        {
            Require();

            await using var scope = Scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NOVAxisDbContext>();

            var playlist = await db.Playlists
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

            if (playlist == null)
                throw new PlaylistException(PlaylistFailure.NotFound, "Takový playlist neznám");

            if (playlist.OwnerId != ownerId)
                throw new PlaylistException(PlaylistFailure.NotYours, "Sdílet ho může jen jeho autor");

            playlist.GuildId = guildId;
            playlist.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);

            return playlist;
        }

        /// <summary>Names the caller can open here, for a command's autocomplete.</summary>
        public async Task<IReadOnlyList<string>> SuggestAsync(
            string prefix, ulong ownerId, ulong? guildId, int limit = 25,
            CancellationToken cancellationToken = default)
        {
            if (!Active)
                return [];

            await using var scope = Scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<NOVAxisDbContext>();

            var names = await Visible(db, ownerId, guildId)
                .Select(x => x.Name)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(prefix))
                return names.Take(limit).ToList();

            return names
                .Where(x => x.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToList();
        }

        private static IQueryable<Playlist> Visible(NOVAxisDbContext db, ulong ownerId, ulong? guildId)
        {
            return db.Playlists.Where(x =>
                x.OwnerId == ownerId ||
                guildId != null && x.GuildId == guildId);
        }

        /// <summary>
        /// Position is stored rather than relied upon, so the order has to be asked for -
        /// a queue which comes back shuffled is not the queue that was saved.
        /// </summary>
        private static void Sort(Playlist playlist)
        {
            playlist?.Tracks.Sort((a, b) => a.Position.CompareTo(b.Position));
        }

        private void Require()
        {
            if (!Active)
                throw new PlaylistException(PlaylistFailure.Disabled, "Playlisty jsou vypnuté");
        }
    }
}
