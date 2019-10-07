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
        private static class MALJson
        {
            public const string API = "https://api.jikan.moe/v3/{0}";

            public abstract class MALResult
            {
                public class Info
                {
                    public class Genre
                    {
                        public int mal_id { get; set; }
                        public string type { get; set; }
                        public string name { get; set; }
                        public string url { get; set; }
                    }

                    public string title_english { get; set; }
                    public List<string> title_synonyms { get; set; }
                    public string title_japanese { get; set; }
                    public string status { get; set; }
                    public string type { get; set; }
                    public string rating { get; set; }
                    public int? rank { get; set; }
                    public List<Genre> genres { get; set; }
                }

                public class Anime : MALResult
                {
                    public bool airing { get; set; }
                    public string aired
                    {
                        get => base.published;
                    }

                    public int episodes { get; set; }
                    public string rated { get; set; }

                    protected override string api
                    {
                        get => string.Format(API, "anime/{0}");
                    }
                }

                public class Manga : MALResult
                {
                    public bool publishing { get; set; }
                    public new string published
                    {
                        get => base.published;
                    }

                    public int chapters { get; set; }
                    public int volumes { get; set; }

                    protected override string api
                    {
                        get => string.Format(API, "manga/{0}");
                    }
                }

                protected virtual string published
                {
                    get => $"from `{start_date?.ToShortDateString() ?? "?"}`\n" +
                        $"to `{end_date?.ToShortDateString() ?? "?"}`";
                }

                public virtual async Task<Info> GetInfo()
                {
                    string api = string.Format(this.api, mal_id);
                    api = Uri.EscapeUriString(api);

                    using (WebClient client = new WebClient() { Encoding = Encoding.UTF8 })
                    {
                        Task<string> result = client.DownloadStringTaskAsync(api);
                        return JObject.Parse(await result).ToObject<Info>();
                    }
                }

                protected abstract string api { get; }

                public Info info { get; set; }
                public int mal_id { get; set; }
                public string url { get; set; }
                public string image_url { get; set; }
                public string title { get; set; }
                public string synopsis { get; set; }
                public string type { get; set; }
                public float score { get; set; }
                public DateTime? start_date { get; set; }
                public DateTime? end_date { get; set; }
                public int members { get; set; }
            }

            public static List<T> Get<T>(string json) where T : MALResult
            {
                JObject mainObject = JObject.Parse(json);
                JArray dataArray = (JArray)mainObject["results"];

                return dataArray.ToObject<List<T>>();
            }
        }

        public InteractiveService InteractiveService { get; set; }

        private async Task ShowResults<T>(List<T> results) where T : MALJson.MALResult
        {
            if (results.Count <= 0)
                throw new Exception("Výsledek databáze neobsahuje žádný prvek");

            EmbedFieldBuilder[] embedFields = new EmbedFieldBuilder[results.Count];

            for (int i = 0; i < results.Count; i++)
            {
                T element = results[i];

                if (element is MALJson.MALResult.Anime)
                {
                    var mal = element as MALJson.MALResult.Anime;

                    embedFields[i] = new EmbedFieldBuilder
                    {
                        Name = $"**[{i + 1}]** {mal.title}",
                        IsInline = false,
                        Value = $"*({mal.start_date?.Year.ToString() ?? "?"})* " +
                        $"{mal.type} : {(mal.airing ? "Airing" : "Finished")} " +
                        $"| Episodes: {mal.episodes} " +
                        $"| Score: {mal.score}/10"
                    };
                }

                else if (element is MALJson.MALResult.Manga)
                {
                    var mal = element as MALJson.MALResult.Manga;

                    embedFields[i] = new EmbedFieldBuilder
                    {
                        Name = $"**[{i + 1}]** {mal.title}",
                        IsInline = false,
                        Value = $"*({mal.start_date?.Year.ToString() ?? "?"})* " +
                        $"{mal.type} : {(mal.publishing ? "Publishing" : "Finished")} " +
                        $"| Volumes: {mal.volumes} " +
                        $"| Score: {mal.score}/10"
                    };
                }

                else
                    throw new NotImplementedException();
            }

            await ReplyAsync(embed: new EmbedBuilder()
                .WithTitle($"**Výsledek databáze serveru MyAnimeList.net** (počet výsledků: {results.Count})")
                .WithDescription("(Proveďte výběr pro více informací)")
                .WithColor(150, 0, 150)
                .WithFields(embedFields).Build());
        }

        [Command("anime"), Summary("Searches for anime in MyAnimeList.net database")]
        public async Task SearchAnime(string name)
        {
            ushort limit = 5;

            if (name.Length < 3)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"(Argument musí být delší než tři znaky)")
                    .WithTitle($"Mé jádro nebylo schopno příjmout daný prvek").Build());

                return;
            }

            string api = string.Format(MALJson.API, "search/anime?q={0}&limit={1}");
            api = string.Format(api, name, limit);
            api = Uri.EscapeUriString(api);

            using (WebClient client = new WebClient() { Encoding = Encoding.UTF8 })
            {
                try
                {
                    string result = await client.DownloadStringTaskAsync(api);

                    List<MALJson.MALResult.Anime> collection = 
                        MALJson.Get<MALJson.MALResult.Anime>(result);

                    await ShowResults(collection);
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
                                mal.info = await mal.GetInfo();

                                await ReplyAsync(embed: new EmbedBuilder()
                                    .WithAuthor($"{mal.title} (#{mal.info.rank?.ToString() ?? "?"})", url: mal.url)
                                    .WithTitle(mal.info.title_japanese)
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
                                            Name = "Status:",
                                            Value = mal.info.status,
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Aired:",
                                            Value = $"{mal.aired}",
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
                                        },                                       

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Rating:",
                                            Value = mal.info.rating,
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Genre:",
                                            Value = string.Join(", ", mal.info.genres.Select(x => x.name)),
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
                        await InteractiveService.ReplyAndDeleteAsync(Context, null,
                            timeout: new TimeSpan(0, 0, 5),
                            embed: new EmbedBuilder()
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
        public async Task SearchManga(string name)
        {
            ushort limit = 5;

            if (name.Length < 3)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"(Argument musí být delší než tři znaky)")
                    .WithTitle($"Mé jádro nebylo schopno příjmout daný prvek").Build());

                return;
            }

            string api = string.Format(MALJson.API, "search/manga?q={0}&limit={1}");
            api = string.Format(api, name, limit);
            api = Uri.EscapeUriString(api);

            using (WebClient client = new WebClient() { Encoding = Encoding.UTF8 })
            {
                try
                {
                    string result = await client.DownloadStringTaskAsync(api);

                    List<MALJson.MALResult.Manga> collection = 
                        MALJson.Get<MALJson.MALResult.Manga>(result);

                    await ShowResults(collection);
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
                                mal.info = await mal.GetInfo();

                                await ReplyAsync(embed: new EmbedBuilder()
                                    .WithAuthor($"{mal.title} (#{mal.info.rank?.ToString() ?? "?"})", url: mal.url)
                                    .WithTitle(mal.info.title_japanese)
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
                                            Name = "Status:",
                                            Value = mal.info.status,
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Published:",
                                            Value = $"{mal.published}",
                                            IsInline = true
                                        },

                                        new EmbedFieldBuilder
                                        {
                                            Name = "Score:",
                                            Value = $"{mal.score}/10",
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
                                            Name = "Genre:",
                                            Value = string.Join(", ", mal.info.genres.Select(x => x.name)),
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
                        await InteractiveService.ReplyAndDeleteAsync(Context, null, 
                            timeout: new TimeSpan(0, 0, 5),
                            embed: new EmbedBuilder()
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
