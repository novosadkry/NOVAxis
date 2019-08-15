using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Net;

using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Addons.Interactive;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;

namespace NOVAxis.Modules
{
    [Group("mal")]
    [RequireUserPermission(GuildPermission.CreateInstantInvite)]
    public class MALModule : InteractiveBase<SocketCommandContext>
    {
        private abstract class MALJson
        {
            public const string API = "https://api.jikan.moe/v3/search/{0}?q={1}&limit={2}";

            public abstract class MALResult
            {
                public class Anime : MALResult
                {
                    public bool airing { get; set; }
                    public int episodes { get; set; }
                    public string rated { get; set; }
                }

                public class Manga : MALResult
                {
                    public bool publishing { get; set; }
                    public int chapters { get; set; }
                    public int volumes { get; set; }
                }

                public int mal_id { get; set; }
                public string url { get; set; }
                public string image_url { get; set; }
                public string title { get; set; }
                public string synopsis { get; set; }
                public string type { get; set; }
                public double score { get; set; }
                public DateTime start_date { get; set; }
                public DateTime? end_date { get; set; }
                public int members { get; set; }
            }
        }

        private class MALJson<T> : MALJson, IEnumerable<T> where T : MALJson.MALResult
        {
            public readonly string json;

            public MALJson(string json)
            {
                this.json = json;
            }

            IEnumerable<T> Convert()
            {
                JObject mainObject = JObject.Parse(json);
                JArray dataArray = (JArray)mainObject["results"];

                return dataArray.ToObject<IEnumerable<T>>();
            }

            public IEnumerator<T> GetEnumerator()
            {
                return Convert().GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        public InteractiveService InteractiveService { get; set; }

        [Command("anime"), Summary("Searches for anime in MyAnimeList.net database")]
        public async Task SearchAnime(string name, ushort limit = 5)
        {
            if (limit < 1)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"(Argument nesmí být menší nebo roven nule)")
                    .WithTitle($"Zajímalo by mě, co zjistíš z nula a méně prvků...").Build());

                return;
            }

            string api = string.Format(MALJson.API, "anime", name, limit);
            api = Uri.EscapeUriString(api);

            using (WebClient client = new WebClient() { Encoding = Encoding.UTF8 })
            {
                try
                {
                    string result = await client.DownloadStringTaskAsync(api);

                    List<MALJson.MALResult.Anime> collection = new MALJson<MALJson.MALResult.Anime>(result).ToList();

                    if (collection.Count <= 0)
                        throw new Exception("Výsledek databáze neobsahuje žádný prvek");

                    EmbedFieldBuilder[] embedFields = new EmbedFieldBuilder[collection.Count];

                    for (int i = 0; i < collection.Count; i++)
                    {
                        MALJson.MALResult.Anime mal = collection[i];

                        embedFields[i] = new EmbedFieldBuilder
                        {
                            Name = $"[{i + 1}] {mal.title}",
                            IsInline = false,
                            Value = $"{mal.type} : {(mal.airing ? "Airing" : "Finished")} " +
                            $"| {mal.start_date.ToShortDateString()} to {mal.end_date?.ToShortDateString() ?? "?"} " +
                            $"| Episodes: {mal.episodes}"
                        };
                    }

                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithTitle($"**Výsledek databáze serveru MyAnimeList.net** (počet výsledků: {collection.Count})")
                        .WithDescription("(Proveďte výběr pro více informací)")
                        .WithColor(150, 0, 150)
                        .WithFields(embedFields).Build());

                    var input = await NextMessageAsync();

                    try
                    {
                        if (input != null)
                        {
                            if (ushort.TryParse(input.Content, out ushort select))
                            {
                                if (select > collection.Count || select <= 0)
                                    throw new Exception("Neplatný výběr");

                                MALJson.MALResult.Anime mal = collection[select - 1];

                                await ReplyAsync(embed: new EmbedBuilder()
                                    .WithTitle(mal.title)
                                    .WithUrl(mal.url)
                                    .WithThumbnailUrl(mal.image_url)
                                    .WithDescription(mal.synopsis)
                                    .WithColor(150, 0, 150)
                                    .WithFields(
                                        new EmbedFieldBuilder
                                        {
                                            Name = "Type:",
                                            Value = mal.type,
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Aired:",
                                            Value = $"`{mal.start_date.ToShortDateString()} to {mal.end_date?.ToShortDateString() ?? "?"}`",
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Episodes:",
                                            Value = mal.episodes,
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Score:",
                                            Value = $"{mal.score}/10",
                                            IsInline = true
                                        }
                                ).Build());
                            }

                            else
                                throw new Exception("Vstup nemá správný formát");
                        }

                        else
                            throw new Exception("Výběr nebyl proveden v časovém limitu");
                    }

                    catch (Exception e)
                    {
                        await ReplyAsync(embed: new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription($"({e.Message})")
                            .WithTitle($"Mé jádro přerušilo čekání na lidský vstup").Build());
                    }              
                }

                catch (Exception e)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription($"({e.Message})")
                        .WithTitle($"V databázi serveru MyAnimeList.net nebyla nalezena shoda").Build());
                }
            }
        }

        [Command("manga"), Summary("Searches for manga in MyAnimeList.net database")]
        public async Task SearchManga(string name, ushort limit = 5)
        {
            if (limit < 1)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"(Argument nesmí být menší nebo roven nule)")
                    .WithTitle($"Mé jádro nebylo schopno příjmout daný prvek").Build());

                return;
            }

            if (name.Length < 3)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"(Argument musí být delší než tři znaky)")
                    .WithTitle($"Mé jádro nebylo schopno příjmout daný prvek").Build());

                return;
            }

            string api = string.Format(MALJson.API, "manga", name, limit);
            api = Uri.EscapeUriString(api);

            using (WebClient client = new WebClient() { Encoding = Encoding.UTF8 })
            {
                try
                {
                    string result = await client.DownloadStringTaskAsync(api);

                    List<MALJson.MALResult.Manga> collection = new MALJson<MALJson.MALResult.Manga>(result).ToList();

                    if (collection.Count <= 0)
                        throw new Exception("Výsledek databáze neobsahuje žádný prvek");

                    EmbedFieldBuilder[] embedFields = new EmbedFieldBuilder[collection.Count];

                    for (int i = 0; i < collection.Count; i++)
                    {
                        MALJson.MALResult.Manga mal = collection[i];

                        embedFields[i] = new EmbedFieldBuilder
                        {
                            Name = $"[{i + 1}] {mal.title}",
                            IsInline = false,
                            Value = $"{mal.type} : {(mal.publishing ? "Publishing" : "Finished")} " +
                            $"| {mal.start_date.ToShortDateString()} to {mal.end_date?.ToShortDateString() ?? "?"} " +
                            $"| Volumes: {mal.volumes}"
                        };
                    }

                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithTitle($"**Výsledek databáze serveru MyAnimeList.net** (počet výsledků: {collection.Count})")
                        .WithDescription("(Proveďte výběr pro více informací)")
                        .WithColor(150, 0, 150)
                        .WithFields(embedFields).Build());

                    var input = await NextMessageAsync();

                    try
                    {
                        if (input != null)
                        {
                            if (ushort.TryParse(input.Content, out ushort select))
                            {
                                if (select > collection.Count || select <= 0)
                                    throw new Exception("Neplatný výběr");

                                MALJson.MALResult.Manga mal = collection[select - 1];

                                await ReplyAsync(embed: new EmbedBuilder()
                                    .WithTitle(mal.title)
                                    .WithUrl(mal.url)
                                    .WithThumbnailUrl(mal.image_url)
                                    .WithDescription(mal.synopsis)
                                    .WithColor(150, 0, 150)
                                    .WithFields(
                                        new EmbedFieldBuilder
                                        {
                                            Name = "Type:",
                                            Value = mal.type,
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Aired:",
                                            Value = $"`{mal.start_date.ToShortDateString()} to {mal.end_date?.ToShortDateString() ?? "?"}`",
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Volumes:",
                                            Value = mal.volumes,
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Chapters:",
                                            Value = mal.chapters,
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Score:",
                                            Value = $"{mal.score}/10",
                                            IsInline = true
                                        }
                                ).Build());
                            }

                            else
                                throw new Exception("Vstup nemá správný formát");
                        }

                        else
                            throw new Exception("Výběr nebyl proveden v časovém limitu");
                    }

                    catch (Exception e)
                    {
                        await ReplyAsync(embed: new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription($"({e.Message})")
                            .WithTitle($"Mé jádro přerušilo čekání na lidský vstup").Build());
                    }
                }

                catch (Exception e)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription($"({e.Message})")
                        .WithTitle($"V databázi serveru MyAnimeList.net nebyla nalezena shoda").Build());
                }
            }
        }
    }
}
