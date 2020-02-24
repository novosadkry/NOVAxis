using System.Collections.Generic;
using System.Linq;

using Discord;
using SharpLink;

namespace NOVAxis.Extensions
{
    public static class AudioModuleExtensions
    {
        public static string GetThumbnailUrl(this LavalinkTrack track)
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
