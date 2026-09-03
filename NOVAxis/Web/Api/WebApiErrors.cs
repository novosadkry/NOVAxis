using Microsoft.AspNetCore.Http;

using System;

using NOVAxis.Services.Audio;
using NOVAxis.Services.Download;

namespace NOVAxis.Web.Api
{
    public record ErrorDto(string Code, string Message);

    /// <summary>
    /// The error shape every endpoint answers with - a stable machine-readable code
    /// for the frontend to branch on, and a message it can show as is.
    /// </summary>
    public static class WebApiErrors
    {
        public static IResult BadRequest(string message)
            => Error(StatusCodes.Status400BadRequest, "bad_request", message);

        public static IResult NotMember()
            => Error(StatusCodes.Status403Forbidden, "not_member", "Nejste členem tohoto serveru");

        public static IResult NothingFound()
            => Error(StatusCodes.Status409Conflict, "nothing_found", "Nepodařilo se nic najít");

        public static IResult ServiceUnavailable()
            => Error(StatusCodes.Status503ServiceUnavailable, "service_unavailable", "Služba není momentálně dostupná");

        /// <summary>
        /// The action is not the caller's alone to take. Conflict rather than Forbidden:
        /// nothing is wrong with who they are, only with doing it without asking.
        /// </summary>
        public static IResult NeedsAVote(string message)
        {
            return Results.Json(new { code = "needs_vote", message }, statusCode: 409);
        }

        public static IResult NotFound(string message)
            => Error(StatusCodes.Status404NotFound, "not_found", message);

        /// <summary>
        /// What a download refused, in the caller's terms. yt-dlp's own words never reach
        /// here - they name cookie files and local paths.
        /// </summary>
        public static IResult From(DownloadException exception)
        {
            return exception.Reason switch
            {
                DownloadFailure.TooLarge
                    => Error(StatusCodes.Status413RequestEntityTooLarge, "too_large", exception.Message),

                DownloadFailure.QuotaExceeded
                    => Error(StatusCodes.Status429TooManyRequests, "quota_exceeded", exception.Message),

                DownloadFailure.Busy
                    => Error(StatusCodes.Status409Conflict, "download_busy", exception.Message),

                DownloadFailure.StorageFull
                    => Error(StatusCodes.Status507InsufficientStorage, "storage_full", exception.Message),

                DownloadFailure.Unsupported
                    => Error(StatusCodes.Status400BadRequest, "bad_request", exception.Message),

                DownloadFailure.Timeout or DownloadFailure.Stalled
                    => Error(StatusCodes.Status504GatewayTimeout, "download_failed", exception.Message),

                _ => Error(StatusCodes.Status502BadGateway, "download_failed", exception.Message)
            };
        }

        public static IResult From(AudioPlayerRetrieveResult result)
        {
            return result.Status switch
            {
                AudioPlayerRetrieveStatus.UserNotInVoiceChannel
                    => Error(StatusCodes.Status409Conflict, "user_not_in_voice",
                        "Nejdříve se připojte do hlasového kanálu"),

                AudioPlayerRetrieveStatus.VoiceChannelMismatch
                    => Error(StatusCodes.Status409Conflict, "voice_channel_mismatch",
                        "Bot právě hraje v jiném hlasovém kanálu"),

                AudioPlayerRetrieveStatus.BotNotConnected
                    => Error(StatusCodes.Status409Conflict, "bot_not_connected",
                        "Bot není připojen do hlasového kanálu"),

                AudioPlayerRetrieveStatus.PreconditionFailed
                    => Error(StatusCodes.Status409Conflict, "precondition_failed",
                        "Akci nyní nelze provést"),

                _ => Error(StatusCodes.Status500InternalServerError, "unknown",
                    "Nastala neznámá chyba")
            };
        }

        private static IResult Error(int status, string code, string message)
            => Results.Json(new ErrorDto(code, message), statusCode: status);
    }
}
