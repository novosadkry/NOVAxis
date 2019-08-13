using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;

using SharpLink;

namespace NOVAxis.Services
{
    public static class AudioModuleExtensions
    {
        public static string GetThumbnailUrl(this LavalinkTrack track)
        {
            string id = System.Web.HttpUtility.ParseQueryString(track.Url)[0];
            return $"https://img.youtube.com/vi/{id}/hqdefault.jpg";
        }
    }

    public class AudioModuleService
    {
        public class Context
        {
            public Context(ulong id)
            {
                GuildId = id;
            }

            public class Track
            {
                public LavalinkTrack Value { get; set; }
                public IUser RequestedBy { get; set; }
                public string ThumbnailUrl { get => Value.GetThumbnailUrl(); }

                public static implicit operator LavalinkTrack(Track track)
                {    
                    return track.Value;
                }
            }

            public LavalinkPlayer GetPlayer() => LavalinkService.Manager.GetPlayer(GuildId);

            public List<Track> Queue { get; set; } = new List<Track>();

            public Track CurrentTrack { get => Queue.First(); }
            public Track LastTrack { get => Queue.Last(); }

            public uint Volume { get; set; } = 100;
            public ulong GuildId { get; }

            public IMessageChannel BoundChannel { get; set; }
        }

        public AudioModuleService()
        {
            guilds = new Dictionary<ulong, Context>();

            LavalinkService.Manager.TrackEnd -= AudioModuleService_TrackEnd;
            LavalinkService.Manager.TrackEnd += AudioModuleService_TrackEnd;
        }

        private IDictionary<ulong, Context> guilds;

        public Context this[ulong id]
        {
            get
            {
                if (!guilds.ContainsKey(id))
                    guilds[id] = new Context(id);

                return guilds[id];
            }

            set
            {
                guilds[id] = value;
            }
        }

        private async Task AudioModuleService_TrackEnd(LavalinkPlayer player, LavalinkTrack track, string _)
        {
            var service = guilds[player.VoiceChannel.GuildId];

            if (service.Queue.Count == 0)
                return;

            service.Queue.RemoveAt(0);

            if (service.Queue.Count > 0)
                await player.PlayAsync(service.Queue.First().Value);

            if (service.Queue.Count != 0)
            {
                await service.BoundChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(150, 0, 150)
                    .WithAuthor("Právě přehrávám:")
                    .WithTitle($"{new Emoji("\u25B6")} {service.LastTrack.Value.Title}")
                    .WithUrl(service.Queue.First().Value.Url)
                    .WithThumbnailUrl(service.Queue.First().ThumbnailUrl)
                    .WithFields(
                        new EmbedFieldBuilder
                        {
                            Name = "Autor:",
                            Value = service.Queue.First().Value.Author,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Délka:",
                            Value = $"`{service.Queue.First().Value.Length}`",
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Vyžádal",
                            Value = service.Queue.First().RequestedBy.Mention,
                            IsInline = true
                        },

                        new EmbedFieldBuilder
                        {
                            Name = "Hlasitost",
                            Value = $"{service.Volume}%",
                            IsInline = true
                        }
                    ).Build());
            }

            else
            {
                await service.BoundChannel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(150, 0, 150)
                    .WithTitle($"Stream audia byl úspěšně dokončen").Build());
            }
        }
    }
}
