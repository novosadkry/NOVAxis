using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NOVAxis.Services.Audio;
using NOVAxis.Services.Playlists;
using NOVAxis.Web.Auth;
using NOVAxis.Web.Contracts;

using Discord.WebSocket;

namespace NOVAxis.Web.Api
{
    public static class PlaylistEndpoints
    {
        public static IEndpointRouteBuilder MapPlaylistApi(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/playlists")
                .RequireAuthorization()
                .RequireRateLimiting(WebRateLimits.Write);

            group.MapGet("/", List);
            group.MapGet("/{id}", GetOne);
            group.MapPost("/", Save);
            group.MapPost("/new", Create);
            group.MapPost("/{id}/tracks", AddTrack);
            group.MapDelete("/{id}/tracks/{trackId}", RemoveTrack);
            group.MapPost("/{id}/load", Load);
            group.MapPost("/{id}/share", Share);
            group.MapDelete("/{id}", Delete);

            return routes;
        }

        /// <summary>
        /// Everything the caller can open. A guild may be named, in which case what has
        /// been shared with it comes too - without one, only their own.
        /// </summary>
        private static async Task<IResult> List(
            ClaimsPrincipal principal,
            PlaylistService playlists,
            string guildId = null)
        {
            if (!playlists.Active)
                return WebApiErrors.ServiceUnavailable();

            var userId = principal.GetDiscordId();
            var found = await playlists.ListAsync(userId, Guild(guildId));

            return Results.Ok(found
                .Select(x => PlaylistDto.FromPlaylist(x, userId, tracks: false))
                .ToList());
        }

        private static async Task<IResult> GetOne(
            string id,
            ClaimsPrincipal principal,
            PlaylistService playlists,
            string guildId = null)
        {
            if (!playlists.Active)
                return WebApiErrors.ServiceUnavailable();

            if (!ulong.TryParse(id, out var playlistId))
                return WebApiErrors.NotFound("Takový playlist neznám");

            var userId = principal.GetDiscordId();
            var playlist = await playlists.GetAsync(playlistId, userId, Guild(guildId));

            return playlist == null
                ? WebApiErrors.NotFound("Takový playlist neznám")
                : Results.Ok(PlaylistDto.FromPlaylist(playlist, userId, tracks: true));
        }

        /// <summary>
        /// Saves what the guild's player currently holds, under a name - replacing a
        /// playlist of the caller's own by that name. Building one up a track at a time
        /// goes through Create and AddTrack instead.
        /// </summary>
        private static async Task<IResult> Save(
            SavePlaylistRequest request,
            ClaimsPrincipal principal,
            PlaylistService playlists,
            WebPlayerService player,
            GuildAccessService access)
        {
            if (!playlists.Active)
                return WebApiErrors.ServiceUnavailable();

            if (!ulong.TryParse(request?.GuildId, out var guildId))
                return WebApiErrors.BadRequest("Chybí server");

            var userId = principal.GetDiscordId();
            var user = await access.GetGuildUserAsync(guildId, userId);

            if (user == null)
                return WebApiErrors.NotMember();

            return await player.WithPlayerAsync(principal, guildId, async current =>
            {
                var tracks = new List<AudioTrack>();

                if (current.CurrentTrack != null)
                    tracks.Add(current.CurrentTrack);

                tracks.AddRange(current.Queue.Select(x => x.Track));

                return await Run(async () =>
                {
                    var saved = await playlists.SaveAsync(
                        userId, user.DisplayName,
                        request.Share ? guildId : null,
                        request.Name, tracks);

                    return Results.Ok(PlaylistDto.FromPlaylist(saved, userId, tracks: false));
                });
            });
        }

        /// <summary>
        /// An empty playlist to fill by searching. Saving takes the queue and refuses an
        /// empty one; this is the other way in.
        /// </summary>
        private static async Task<IResult> Create(
            CreatePlaylistRequest request,
            ClaimsPrincipal principal,
            PlaylistService playlists,
            DiscordShardedClient client)
        {
            if (!playlists.Active)
                return WebApiErrors.ServiceUnavailable();

            var userId = principal.GetDiscordId();
            var name = client.GetUser(userId)?.GlobalName ?? client.GetUser(userId)?.Username;

            return await Run(async () =>
            {
                var created = await playlists.CreateAsync(userId, name, request?.Name);
                return Results.Ok(PlaylistDto.FromPlaylist(created, userId, tracks: true));
            });
        }

        private static async Task<IResult> AddTrack(
            string id,
            AddTrackRequest request,
            ClaimsPrincipal principal,
            PlaylistService playlists)
        {
            if (!playlists.Active)
                return WebApiErrors.ServiceUnavailable();

            if (!ulong.TryParse(id, out var playlistId))
                return WebApiErrors.NotFound("Takový playlist neznám");

            var track = request?.ToTrack();

            if (track == null)
                return WebApiErrors.BadRequest("Skladbě chybí jméno nebo odkaz");

            var userId = principal.GetDiscordId();

            return await Run(async () =>
            {
                var updated = await playlists.AddTrackAsync(playlistId, userId, track);
                return Results.Ok(PlaylistDto.FromPlaylist(updated, userId, tracks: true));
            });
        }

        private static async Task<IResult> RemoveTrack(
            string id, string trackId, ClaimsPrincipal principal, PlaylistService playlists)
        {
            if (!playlists.Active)
                return WebApiErrors.ServiceUnavailable();

            if (!ulong.TryParse(id, out var playlistId) || !ulong.TryParse(trackId, out var track))
                return WebApiErrors.NotFound("Takovou skladbu tam nemám");

            var userId = principal.GetDiscordId();

            return await Run(async () =>
            {
                var updated = await playlists.RemoveTrackAsync(playlistId, userId, track);
                return Results.Ok(PlaylistDto.FromPlaylist(updated, userId, tracks: true));
            });
        }

        private static async Task<IResult> Load(
            string id,
            LoadPlaylistRequest request,
            ClaimsPrincipal principal,
            PlaylistService playlists,
            WebPlayerService player)
        {
            if (!playlists.Active)
                return WebApiErrors.ServiceUnavailable();

            if (!ulong.TryParse(id, out var playlistId) ||
                !ulong.TryParse(request?.GuildId, out var guildId))
                return WebApiErrors.BadRequest("Chybí server");

            var userId = principal.GetDiscordId();
            var playlist = await playlists.GetAsync(playlistId, userId, guildId);

            if (playlist == null)
                return WebApiErrors.NotFound("Takový playlist neznám");

            if (playlist.Tracks.Count == 0)
                return WebApiErrors.BadRequest("Playlist je prázdný");

            return await player.EnqueueAsync(
                principal, guildId,
                playlist.Tracks.Select(x => x.ToTrack()).ToList(),
                request.Replace);
        }

        private static async Task<IResult> Share(
            string id,
            SharePlaylistRequest request,
            ClaimsPrincipal principal,
            PlaylistService playlists,
            GuildAccessService access)
        {
            if (!playlists.Active)
                return WebApiErrors.ServiceUnavailable();

            if (!ulong.TryParse(id, out var playlistId))
                return WebApiErrors.NotFound("Takový playlist neznám");

            var userId = principal.GetDiscordId();
            ulong? guildId = null;

            if (request?.Shared == true)
            {
                if (!ulong.TryParse(request.GuildId, out var target))
                    return WebApiErrors.BadRequest("Chybí server");

                // Sharing into a guild you are not in would put it in front of strangers
                if (await access.GetGuildUserAsync(target, userId) == null)
                    return WebApiErrors.NotMember();

                guildId = target;
            }

            return await Run(async () =>
            {
                var updated = await playlists.ShareAsync(playlistId, userId, guildId);
                return Results.Ok(PlaylistDto.FromPlaylist(updated, userId, tracks: false));
            });
        }

        private static async Task<IResult> Delete(
            string id, ClaimsPrincipal principal, PlaylistService playlists)
        {
            if (!playlists.Active)
                return WebApiErrors.ServiceUnavailable();

            if (!ulong.TryParse(id, out var playlistId))
                return WebApiErrors.NotFound("Takový playlist neznám");

            return await Run(async () =>
            {
                await playlists.DeleteAsync(playlistId, principal.GetDiscordId());
                return Results.NoContent();
            });
        }

        /// <summary>Turns the store's refusals into answers rather than five hundreds.</summary>
        private static async Task<IResult> Run(Func<Task<IResult>> action)
        {
            try
            {
                return await action();
            }
            catch (PlaylistException e)
            {
                return e.Failure switch
                {
                    PlaylistFailure.NotFound => WebApiErrors.NotFound(e.Message),
                    PlaylistFailure.NotYours => WebApiErrors.NotFound(e.Message),
                    PlaylistFailure.Disabled => WebApiErrors.ServiceUnavailable(),
                    _ => WebApiErrors.BadRequest(e.Message)
                };
            }
        }

        private static ulong? Guild(string guildId)
        {
            return ulong.TryParse(guildId, out var parsed) ? parsed : null;
        }
    }
}
