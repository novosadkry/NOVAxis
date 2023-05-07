using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

using NOVAxis.Utilities;
using NOVAxis.Preconditions;

using Discord;
using Discord.Interactions;

using Newtonsoft.Json.Linq;

namespace NOVAxis.Modules.MAL
{
    [Cooldown(5)]
    [Group("mal", "Shows results from MyAnimeList.net")]
    public class MALModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public const string API = "https://api.jikan.moe/v4/{0}";
        public InteractionCache InteractionCache { get; set; }

        [SlashCommand("anime", "Searches for anime in MyAnimeList.net database")]
        public async Task CmdSearchAnime(string name)
        {
            const ushort limit = 5;

            if (name.Length < 3)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Argument musí být delší než tři znaky)")
                    .WithTitle("Mé jádro nebylo schopno příjmout daný prvek").Build());

                return;
            }

            string api = string.Format(API, "anime?q={0}&limit={1}");
            api = string.Format(api, name, limit);
            api = Uri.EscapeUriString(api);

            using var client = new HttpClient();
            client.BaseAddress = new Uri(api);

            string result = await client.GetStringAsync(api);

            dynamic root = JObject.Parse(result);
            var collection = root.data;

            var id = InteractionCache.Store(collection);

            EmbedFieldBuilder[] embedFields = new EmbedFieldBuilder[collection.Count];

            for (int i = 0; i < collection.Count; i++)
            {
                var anime = collection[i];
                embedFields[i] = new EmbedFieldBuilder
                {
                    Name = $"**[{i + 1}]** {anime.title}",
                    IsInline = false,
                    Value = $"*({anime.start_date?.Year.ToString() ?? "?"})* " +
                            $"{anime.type} : {((bool)anime.airing ? "Airing" : "Finished")} " +
                            $"| Episodes: {anime.episodes} " + $"| Score: {anime.score}/10"
                };
            }

            var embed = new EmbedBuilder()
                .WithTitle($"**Výsledek databáze serveru MyAnimeList.net** (počet výsledků: {collection.Count})")
                .WithDescription("(Proveďte výběr pro více informací)")
                .WithColor(255, 26, 117)
                .WithFields(embedFields)
                .Build();

            var options = new List<SelectMenuOptionBuilder>();
            for (int i = 0; i < collection.Count; i++)
                options.Add(new SelectMenuOptionBuilder($"{i + 1}", $"{i + 1}"));

            await RespondAsync(
                embed: embed,
                components: new ComponentBuilder()
                    .WithSelectMenu(
                        $"CmdSearchAnime_Select_{id}",
                        options)
                    .Build());
        }

        [ComponentInteraction("CmdSearchAnime_Select_*", true)]
        public async Task CmdSearchAnime_Select(ulong id, string[] selectedIndices)
        {
            if (!int.TryParse(selectedIndices[0], out int index)) return;
            if (Context.Interaction is not IComponentInteraction component) return;

            if (InteractionCache[id] is JArray collection)
            {
                if (index > collection.Count || index <= 0)
                    throw new InvalidOperationException("Invalid index");

                dynamic mal = collection[index - 1];

                await RespondAsync(embed: new EmbedBuilder()
                    .WithAuthor($"{mal.title} (#rank)") // url: mal.url
                    .WithTitle("Title in japanese")
                    //.WithThumbnailUrl(mal.image_url)
                    .WithDescription("synopsis")
                    .WithColor(255, 26, 117)
                    .AddField("Type:", "type", true)
                    .AddField("Status:", "status", true)
                    .AddField("Aired:", "aired", true)
                    .AddField("Episodes:", "episodes", true)
                    .AddField("Score:", "score/10", true)
                    .AddField("Rating:", "rating", true)
                    .AddField("Genres:", "genres", true)
                    .Build());
            }

            else
            {
                await component.UpdateAsync(m =>
                {
                    m.Components = null;
                    m.Embed = new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Vypršel časový limit)")
                        .WithTitle("Mé jádro přerušilo čekání na lidský vstup").Build();
                });
            }
        }

        [SlashCommand("manga", "Searches for manga in MyAnimeList.net database")]
        public async Task CmdSearchManga(string name)
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

            string api = string.Format(API, "search/manga?q={0}&limit={1}");
            api = string.Format(api, name, limit);
            api = Uri.EscapeUriString(api);

            using var client = new HttpClient();
            client.BaseAddress = new Uri(api);

            string result = await client.GetStringAsync(api);

            dynamic root = JObject.Parse(result);
            var collection = root.data;

            var id = InteractionCache.Store(collection);

            EmbedFieldBuilder[] embedFields = new EmbedFieldBuilder[collection.Count];

            for (int i = 0; i < collection.Count; i++)
            {
                var manga = collection[i];
                embedFields[i] = new EmbedFieldBuilder
                {
                    Name = $"**[{i + 1}]** {manga.title}",
                    IsInline = false,
                    Value = $"*({manga.start_date?.Year.ToString() ?? "?"})* " +
                            $"{manga.type} : {(manga.publishing ? "Publishing" : "Finished")} " +
                            $"| Volumes: {manga.volumes} " + $"| Score: {manga.score}/10"
                };
            }

            var embed = new EmbedBuilder()
                .WithTitle($"**Výsledek databáze serveru MyAnimeList.net** (počet výsledků: {collection.Count})")
                .WithDescription("(Proveďte výběr pro více informací)")
                .WithColor(255, 26, 117)
                .WithFields(embedFields)
                .Build();

            await RespondAsync(
                embed: embed,
                components: new ComponentBuilder()
                    .WithSelectMenu(
                        $"CmdSearchManga_Select_{id}",
                        minValues: 1,
                        maxValues: collection.Count,
                        type: ComponentType.TextInput)
                    .Build());
        }

        [ComponentInteraction("CmdSearchManga_Select_*,*", true)]
        public async Task CmdSearchManga_Select(ulong id, int index)
        {
            if (Context.Interaction is not IComponentInteraction component)
                return;

            if (InteractionCache[id] is JArray collection)
            {
                if (index > collection.Count || index <= 0)
                    throw new InvalidOperationException("Invalid index");

                dynamic mal = collection[index - 1];

                await RespondAsync(embed: new EmbedBuilder()
                    .WithAuthor($"{mal.title} (#rank)") // url: mal.url
                    .WithTitle("Title in japanese")
                    //.WithThumbnailUrl(mal.image_url)
                    .WithDescription("synopsis")
                    .WithColor(255, 26, 117)
                    .AddField("Type:", "type", true)
                    .AddField("Status:", "status", true)
                    .AddField("Published:", "published", true)
                    .AddField("Score:", "score/10", true)
                    .AddField("Volumes:", "volumes", true)
                    .AddField("Chapters:", "chapters", true)
                    .AddField("Genre:", "genres", true)
                    .Build());
            }

            else
            {
                await component.UpdateAsync(m =>
                {
                    m.Components = null;
                    m.Embed = new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Vypršel časový limit)")
                        .WithTitle("Mé jádro přerušilo čekání na lidský vstup").Build();
                });
            }
        }
    }
}
