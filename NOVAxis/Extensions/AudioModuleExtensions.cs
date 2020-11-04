using System;
using System.Collections.Generic;
using System.Linq;

using Discord;
using Victoria;

namespace NOVAxis.Extensions
{
    public static class AudioModuleExtensions
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

        public static LinkedListNode<T> RemoveAt<T>(this LinkedList<T> list, int index)
        {
            LinkedListNode<T> currentNode = list.First;
            for (int i = 0; i <= index && currentNode != null; i++)
            {
                if (i == index)
                {
                    list.Remove(currentNode);
                    return currentNode;
                }

                currentNode = currentNode.Next;
            }

            throw new IndexOutOfRangeException();
        }
    }
}
