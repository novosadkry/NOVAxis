using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

using NOVAxis.Preconditions;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NOVAxis.Modules
{
    [Group("jisho")]
    public class JishoModule : ModuleBase<SocketCommandContext>
    {
        private class JishoJson
        {
            public const string API = "https://jisho.org/api/v1/search/words?keyword={0}";

            public string Word { get; set; }
            public string Reading { get; set; }
            public string[][] English_definitions { get; set; }

            public static IEnumerable<JishoJson> Convert(string jsonString, int numberOfEntries)
            {
                JObject mainObject = JObject.Parse(jsonString);
                JArray dataArray = (JArray)mainObject["data"];

                if (numberOfEntries > dataArray.Count)
                    numberOfEntries = dataArray.Count;

                for (int i = 0; i < numberOfEntries; i++)
                {
                    JishoJson json = new JishoJson();
                    JObject dataObject = (JObject)dataArray[i];

                    json.Word = (string)dataObject["japanese"][0]["word"];
                    json.Reading = (string)dataObject["japanese"][0]["reading"];

                    JArray sensesArray = (JArray)dataObject["senses"];
                    json.English_definitions = new string[sensesArray.Count][];

                    for (int j = 0; j < sensesArray.Count; j++)
                    {
                        JArray defArray = (JArray)sensesArray[j]["english_definitions"];
                        json.English_definitions[j] = new string[defArray.Count];

                        for (int k = 0; k < defArray.Count; k++)
                        {
                            json.English_definitions[j][k] = (string)defArray[k];
                        }
                    }

                    yield return json;
                }
            }
        }

        [Command, Summary("Searches through Jisho.org dictionary")]
        public async Task Search(string text, ushort limit = 100)
        {
            if (limit < 1)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"(Argument nesmí být menší nebo roven nule)")
                    .WithTitle($"Mé jádro nebylo schopno příjmout daný prvek").Build());

                return;
            }

            string api = string.Format(JishoJson.API, text);
            api = Uri.EscapeUriString(api);

            using (WebClient client = new WebClient() { Encoding = Encoding.UTF8 })
            {
                try
                {
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
                            Value = $"Meaning:\n {sb.ToString()}"
                        };
                    }

                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithTitle($"**Výsledek databáze serveru Jisho.org** (počet výsledků: {collection.Count})")
                        .WithColor(150, 0, 150)
                        .WithFields(embedFields).Build());
                }


                catch (Exception e)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription($"({e.Message})")
                        .WithTitle($"V databázi serveru Jisho.org nebyla nalezena shoda").Build());
                }
            }
        }
    }
}
