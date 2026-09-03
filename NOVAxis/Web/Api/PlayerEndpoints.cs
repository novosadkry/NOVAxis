using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

using NOVAxis.Services.Audio;
using NOVAxis.Web.Contracts;

namespace NOVAxis.Web.Api
{
    public static class PlayerEndpoints
    {
        public static IEndpointRouteBuilder MapPlayerApi(this IEndpointRouteBuilder routes)
        {
            var group = routes.MapGroup("/api/guilds/{guildId}")
                .RequireAuthorization()
                .RequireRateLimiting(WebRateLimits.Write);

            group.MapPost("/play", Play);
            group.MapPost("/pause", Pause);
            group.MapPost("/resume", Resume);
            group.MapPost("/stop", Stop);
            group.MapPost("/skip", Skip);
            group.MapPost("/seek", Seek);
            group.MapPost("/volume", Volume);
            group.MapPost("/repeat", Repeat);
            group.MapPost("/disconnect", Disconnect);

            group.MapDelete("/queue", ClearQueue);
            group.MapDelete("/queue/{requestId}", RemoveItem);
            group.MapPost("/queue/{requestId}/move", MoveItem);

            return routes;
        }

        private static Task<IResult> Play(
            ulong guildId, PlayRequest request, ClaimsPrincipal user, WebPlayerService player,
            CancellationToken cancellationToken)
        {
            return player.PlayAsync(user, guildId, request?.Query, cancellationToken);
        }

        private static Task<IResult> Pause(ulong guildId, ClaimsPrincipal user, WebPlayerService player)
        {
            return player.ControlAsync(user, guildId,
                p => p.PauseAsync(),
                AudioPrecondition.Playing, AudioPrecondition.NotPaused);
        }

        private static Task<IResult> Resume(ulong guildId, ClaimsPrincipal user, WebPlayerService player)
        {
            return player.ControlAsync(user, guildId,
                p => p.ResumeAsync(),
                AudioPrecondition.Paused);
        }

        private static Task<IResult> Stop(ulong guildId, ClaimsPrincipal user, WebPlayerService player)
        {
            return player.ControlAsync(user, guildId,
                p => p.StopAsync(),
                AudioPrecondition.Playing);
        }

        private static Task<IResult> Skip(
            ulong guildId, SkipRequest request, ClaimsPrincipal user, WebPlayerService player)
        {
            var count = Math.Max(request?.Count ?? 1, 1);

            return player.SkipAsync(user, guildId, count);
        }

        private static Task<IResult> Seek(
            ulong guildId, SeekRequest request, ClaimsPrincipal user, WebPlayerService player)
        {
            var position = TimeSpan.FromMilliseconds(Math.Max(request?.PositionMs ?? 0, 0));

            return player.ControlAsync(user, guildId,
                p => p.SeekAsync(position),
                AudioPrecondition.Playing);
        }

        private static Task<IResult> Volume(
            ulong guildId, VolumeRequest request, ClaimsPrincipal user, WebPlayerService player)
        {
            // The same ceiling the slash command enforces
            var percent = Math.Clamp(request?.Percent ?? 100, 0, 150);

            return player.ControlAsync(user, guildId,
                p => p.SetVolumeAsync(percent / 100f));
        }

        private static Task<IResult> Repeat(
            ulong guildId, RepeatRequest request, ClaimsPrincipal user, WebPlayerService player)
        {
            if (!Enum.TryParse<AudioRepeatMode>(request?.Mode, true, out var mode))
                return Task.FromResult(WebApiErrors.BadRequest("Neznámý režim opakování"));

            return player.ControlAsync(user, guildId, p =>
            {
                p.RepeatMode = mode;
                return ValueTask.CompletedTask;
            });
        }

        private static Task<IResult> Disconnect(ulong guildId, ClaimsPrincipal user, WebPlayerService player)
        {
            return player.ControlAsync(user, guildId, p => p.DisconnectAsync());
        }

        private static Task<IResult> ClearQueue(ulong guildId, ClaimsPrincipal user, WebPlayerService player)
        {
            return player.ControlAsync(user, guildId, p => p.Queue.ClearAsync());
        }

        private static Task<IResult> RemoveItem(
            ulong guildId, ulong requestId, ClaimsPrincipal user, WebPlayerService player)
        {
            return player.ControlAsync(user, guildId,
                p => new ValueTask(p.Queue.RemoveAsync(WebPlayerService.Probe(requestId)).AsTask()));
        }

        private static Task<IResult> MoveItem(
            ulong guildId, ulong requestId, MoveRequest request, ClaimsPrincipal user, WebPlayerService player)
        {
            var toIndex = Math.Max(request?.ToIndex ?? 0, 0);

            return player.ControlAsync(user, guildId,
                p => new ValueTask(p.Queue.MoveAsync(WebPlayerService.Probe(requestId), toIndex).AsTask()));
        }
    }
}
