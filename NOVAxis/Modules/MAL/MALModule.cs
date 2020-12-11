using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;

using Interactivity;

namespace NOVAxis.Modules.MAL
{
    [Group("mal")]
    public class MALModule : ModuleBase<SocketCommandContext>
    {
        public InteractivityService InteractivityService { get; set; }

        private async Task<IUserMessage> ShowResults<T>(List<T> results) where T : MALJson.MALResult
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

            return await ReplyAsync(embed: new EmbedBuilder()
                .WithTitle($"**Výsledek databáze serveru MyAnimeList.net** (počet výsledků: {results.Count})")
                .WithDescription("(Proveďte výběr pro více informací)")
                .WithColor(255, 26, 117)
                .WithFields(embedFields).Build());
        }

        [Command("anime"), Summary("Searches for anime in MyAnimeList.net database")]
        public async Task SearchAnime([Remainder]string name)
        {
            ushort limit = 5;

            if (name.Length < 3)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Argument musí být delší než tři znaky)")
                    .WithTitle("Mé jádro nebylo schopno příjmout daný prvek").Build());

                return;
            }

            string api = string.Format(MALJson.API, "search/anime?q={0}&limit={1}");
            api = string.Format(api, name, limit);
            api = Uri.EscapeUriString(api);

            using WebClient client = new WebClient { Encoding = Encoding.UTF8 };

            try
            {
                string result = await client.DownloadStringTaskAsync(api);

                List<MALJson.MALResult.Anime> collection = 
                    MALJson.Get<MALJson.MALResult.Anime>(result);

                var msg = await ShowResults(collection);
                var input = await InteractivityService.NextMessageAsync(
                    timeout: TimeSpan.FromSeconds(10));

                try
                {
                    if (input.IsSuccess)
                    {
                        if (ushort.TryParse(input.Value.Content, out ushort select))
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
                                .WithColor(255, 26, 117)
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
                    await msg.ModifyAsync(prop =>
                    {
                        prop.Embed = new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription($"({e.Message})")
                            .WithTitle("Mé jádro přerušilo čekání na lidský vstup").Build();
                    });
                }              
            }

            catch (Exception e)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"({e.Message})")
                    .WithTitle("V databázi serveru MyAnimeList.net nebyla nalezena shoda").Build());
            }
        }

        [Command("manga"), Summary("Searches for manga in MyAnimeList.net database")]
        public async Task SearchManga([Remainder]string name)
        {
            ushort limit = 5;

            if (name.Length < 3)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Argument musí být delší než tři znaky)")
                    .WithTitle("Mé jádro nebylo schopno příjmout daný prvek").Build());

                return;
            }

            string api = string.Format(MALJson.API, "search/manga?q={0}&limit={1}");
            api = string.Format(api, name, limit);
            api = Uri.EscapeUriString(api);

            using WebClient client = new WebClient { Encoding = Encoding.UTF8 };

            try
            {
                string result = await client.DownloadStringTaskAsync(api);

                List<MALJson.MALResult.Manga> collection = 
                    MALJson.Get<MALJson.MALResult.Manga>(result);

                var msg = await ShowResults(collection);
                var input = await InteractivityService.NextMessageAsync(
                    timeout: TimeSpan.FromSeconds(10));

                try
                {
                    if (input.IsSuccess)
                    {
                        if (ushort.TryParse(input.Value.Content, out ushort select))
                        {
                            if (select > collection.Count || select <= 0)
                                throw new Exception("Neplatný výběr");

                            MALJson.MALResult.Manga mal = collection[select - 1];
                            mal.info = await mal.GetInfo();

                            msg = await ReplyAsync(embed: new EmbedBuilder()
                                .WithAuthor($"{mal.title} (#{mal.info.rank?.ToString() ?? "?"})", url: mal.url)
                                .WithTitle(mal.info.title_japanese)
                                .WithThumbnailUrl(mal.image_url)
                                .WithDescription(mal.synopsis)
                                .WithColor(255, 26, 117)
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
                    await msg.ModifyAsync(prop =>
                    {
                        prop.Embed = new EmbedBuilder()
                            .WithColor(220, 20, 60)
                            .WithDescription($"({e.Message})")
                            .WithTitle("Mé jádro přerušilo čekání na lidský vstup").Build();
                    });
                }
            }

            catch (Exception e)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"({e.Message})")
                    .WithTitle("V databázi serveru MyAnimeList.net nebyla nalezena shoda").Build());
            }
        }
    }
}
