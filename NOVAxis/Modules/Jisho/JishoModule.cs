using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;

namespace NOVAxis.Modules.Jisho
{
    [Group("jisho")]
    public class JishoModule : ModuleBase<ShardedCommandContext>
    {
        public const string API = "https://jisho.org/api/v1/search/words?keyword={0}";

        [Command, Summary("Searches through Jisho.org dictionary")]
        public async Task Search(string text, ushort limit = 100)
        {
            if (limit < 1)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Argument nesmí být menší nebo roven nule)")
                    .WithTitle("Mé jádro nebylo schopno příjmout daný prvek").Build());

                return;
            }

            string api = string.Format(API, text);
            api = Uri.EscapeUriString(api);

            try
            {
                using WebClient client = new WebClient { Encoding = Encoding.UTF8 };
                string result = await client.DownloadStringTaskAsync(api);

                List<JishoJson> collection = JishoJson.Convert(result, limit).ToList();

                if (collection.Count <= 0)
                    throw new Exception("Výsledek databáze neobsahuje žádný prvek");

                EmbedFieldBuilder[] embedFields = new EmbedFieldBuilder[collection.Count];
                for (int i = 0; i < collection.Count; i++)
                {
                    JishoJson json = collection[i];
                    StringBuilder sb = new StringBuilder();

                    for (int j = 0; j < json.English_definitions.Length; j++)
                    {
                        sb.Append($"{j + 1}: ");
                        sb.Append($"{string.Join(", ", json.English_definitions[j])}" + "\n");
                    }

                    embedFields[i] = new EmbedFieldBuilder
                    {
                        Name = $"Word: {json.Word} | Reading: {json.Reading}",
                        IsInline = false,
                        Value = $"Meaning:\n {sb}"
                    };
                }

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithTitle($"**Výsledek databáze serveru Jisho.org** (počet výsledků: {collection.Count})")
                    .WithColor(255, 26, 117)
                    .WithFields(embedFields).Build());
            }


            catch (Exception e)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"({e.Message})")
                    .WithTitle("V databázi serveru Jisho.org nebyla nalezena shoda").Build());
            }
        }
    }
}
