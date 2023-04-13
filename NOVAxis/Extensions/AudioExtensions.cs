using System.Collections.Generic;
using System.Linq;

using Discord;
using Victoria.Player;

namespace NOVAxis.Extensions
{
    public static class AudioExtensions
    {
        public static string GetThumbnailUrl(this LavaTrack track)
        {
            string id = System.Web.HttpUtility.ParseQueryString(track.Url)[0];
            return $"https://img.youtube.com/vi/{id}/hqdefault.jpg";
        }

        public static IAsyncEnumerable<IGuildUser> GetHumanUsers(this IVoiceChannel voiceChannel)
        {
            return from u in voiceChannel.GetUsersAsync().Flatten()
                   where !u.IsBot
                   select u;
        }
    }
}
