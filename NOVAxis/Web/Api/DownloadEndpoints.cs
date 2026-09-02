using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NOVAxis.Services.Audio.YtDlp;
using NOVAxis.Services.Download;
using NOVAxis.Web.Auth;
using NOVAxis.Web.Contracts;

using Discord.WebSocket;

namespace NOVAxis.Web.Api
{
    public static class DownloadEndpoints
    {
        public static IEndpointRouteBuilder MapDownloadApi(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/downloads")
                .RequireAuthorization();

            group.MapGet("/", GetOverview);
            group.MapGet("/probe", Probe).RequireRateLimiting(WebRateLimits.Search);
            group.MapPost("/", Start).RequireRateLimiting(WebRateLimits.Download);
            group.MapGet("/{id}", GetOne);
            group.MapDelete("/{id}", Revoke).RequireRateLimiting(WebRateLimits.Write);

            // Outside the group on purpose. It is reached by a plain link rather than by
            // fetch, so it answers in redirects rather than json, and it carries no rate
            // limit - a resumed download issues many range requests, and throttling those
            // would break the very thing range support is for.
            routes.MapGet("/api/downloads/{id}/file", GetFile).AllowAnonymous();

            return routes;
        }

        private static async Task<IResult> GetOverview(
            ClaimsPrincipal principal,
            DownloadService downloads,
            DiscordShardedClient client,
            GuildAccessService access)
        {
            var userId = principal.GetDiscordId();

            if (!downloads.Active)
                return WebApiErrors.ServiceUnavailable();

            if (!await SharesAGuildAsync(userId, client, access))
                return WebApiErrors.NotMember();

            return Results.Ok(new DownloadOverviewDto(
                DownloadDto.FromRecord(downloads.ForUser(userId)),
                DownloadQuotaDto.FromQuota(downloads.QuotaFor(userId))));
        }

        private static async Task<IResult> Probe(
            string url,
            ClaimsPrincipal principal,
            DownloadService downloads,
            DiscordShardedClient client,
            GuildAccessService access,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(url))
                return WebApiErrors.BadRequest("Chybí odkaz");

            if (!downloads.Active)
                return WebApiErrors.ServiceUnavailable();

            var userId = principal.GetDiscordId();

            if (!await SharesAGuildAsync(userId, client, access))
                return WebApiErrors.NotMember();

            try
            {
                var info = await downloads.ProbeAsync(url, cancellationToken);

                var formats = downloads.ChoicesFor(info, DownloadKind.Video)
                    .Concat(downloads.ChoicesFor(info, DownloadKind.Audio))
                    .Select(DownloadFormatDto.FromChoice)
                    .ToList();

                return Results.Ok(new DownloadProbeDto(
                    info.Url?.AbsoluteUri ?? url,
                    info.Title,
                    info.Thumbnail?.AbsoluteUri,
                    info.Duration.TotalMilliseconds,
                    info.IsLive,
                    formats));
            }
            catch (DownloadException e)
            {
                return WebApiErrors.From(e);
            }
            catch (Exception e) when (e is ProcessException or HttpRequestException)
            {
                return WebApiErrors.ServiceUnavailable();
            }
        }

        private static async Task<IResult> Start(
            DownloadRequest request,
            ClaimsPrincipal principal,
            DownloadService downloads,
            DiscordShardedClient client,
            GuildAccessService access,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request?.Url))
                return WebApiErrors.BadRequest("Chybí odkaz");

            if (!downloads.Active)
                return WebApiErrors.ServiceUnavailable();

            var userId = principal.GetDiscordId();

            if (!await SharesAGuildAsync(userId, client, access))
                return WebApiErrors.NotMember();

            if (!Enum.TryParse<DownloadKind>(request.Kind, ignoreCase: true, out var kind))
                return WebApiErrors.BadRequest("Neznámý druh stahování");

            try
            {
                var record = await downloads.RequestAsync(
                    userId, request.Url, kind, request.FormatId,
                    title: request.Title, cancellationToken: cancellationToken);

                return Results.Ok(DownloadDto.FromRecord(record));
            }
            catch (DownloadException e)
            {
                return WebApiErrors.From(e);
            }
            catch (Exception e) when (e is ProcessException or HttpRequestException)
            {
                return WebApiErrors.ServiceUnavailable();
            }
        }

        private static IResult GetOne(ulong id, ClaimsPrincipal principal, DownloadService downloads)
        {
            var record = downloads.Find(id);

            // Someone else's download is answered exactly like one which never existed:
            // the ids are ordered and guessable, and a 403 would confirm a hit
            return record == null || record.OwnerId != principal.GetDiscordId()
                ? WebApiErrors.NotFound("Tohle stahování neexistuje")
                : Results.Ok(DownloadDto.FromRecord(record));
        }

        private static async Task<IResult> Revoke(ulong id, ClaimsPrincipal principal, DownloadService downloads)
        {
            return await downloads.RevokeAsync(principal.GetDiscordId(), id)
                ? Results.NoContent()
                : WebApiErrors.NotFound("Tohle stahování neexistuje");
        }

        /// <summary>
        /// Hands the bytes over. Answers failures with a redirect rather than the usual json
        /// error, because a browser following a download link would either render the json
        /// as text or quietly save it as a file.
        /// </summary>
        private static IResult GetFile(
            ulong id,
            ClaimsPrincipal principal,
            DownloadService downloads,
            HttpContext context)
        {
            var userId = principal.GetDiscordId();

            if (userId == 0)
            {
                var returnUrl = Uri.EscapeDataString($"/downloads?d={id}");
                return Results.Redirect($"/api/auth/login?returnUrl={returnUrl}");
            }

            var record = downloads.Find(id);

            if (record == null || record.OwnerId != userId)
                return Failed("not_found");

            if (record.State == DownloadState.Expired)
                return Failed("expired");

            if (record.State == DownloadState.Revoked)
                return Failed("revoked");

            if (record.State != DownloadState.Ready ||
                string.IsNullOrEmpty(record.FilePath) ||
                !File.Exists(record.FilePath))
                return Failed("not_found");

            // The bytes are whatever a stranger's link produced, and they are served from the
            // same origin as the app and its session cookie. Sniffed as html, one of them
            // would run as this site.
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            // The address is the same whoever asks, and only the cookie tells them apart -
            // which is not something a cache in front of this keys on by default
            context.Response.Headers["Cache-Control"] = "private, no-store";

            return Results.File(
                record.FilePath,
                ContentTypeFor(record.FilePath),
                Path.GetFileName(record.FilePath),
                record.CreatedAt.UtcDateTime,
                enableRangeProcessing: true);
        }

        private static IResult Failed(string code)
        {
            return Results.Redirect($"/downloads?error={code}");
        }

        /// <summary>
        /// An allow list rather than a lookup: anything unrecognised is handed over as bytes,
        /// never as something a browser might try to display.
        /// </summary>
        private static string ContentTypeFor(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp4" or ".m4v" => "video/mp4",
                ".webm" => "video/webm",
                ".mkv" => "video/x-matroska",
                ".mov" => "video/quicktime",
                ".gif" => "image/gif",
                ".mp3" => "audio/mpeg",
                ".m4a" => "audio/mp4",
                ".opus" or ".ogg" => "audio/ogg",
                ".flac" => "audio/flac",
                ".wav" => "audio/wav",
                _ => "application/octet-stream"
            };
        }

        /// <summary>
        /// A download is nobody's guild's business, but it is still only for people the bot
        /// actually shares a server with - the same bar the rest of the app applies.
        /// </summary>
        private static async Task<bool> SharesAGuildAsync(
            ulong userId, DiscordShardedClient client, GuildAccessService access)
        {
            if (userId == 0)
                return false;

            foreach (var guild in client.Guilds)
            {
                if (await access.GetGuildUserAsync(guild.Id, userId) != null)
                    return true;
            }

            return false;
        }
    }
}
