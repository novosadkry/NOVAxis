using Microsoft.AspNetCore.Http;

using NOVAxis.Services.Audio;

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
