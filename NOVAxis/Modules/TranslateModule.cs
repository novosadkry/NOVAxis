using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace NOVAxis.Modules
{
    [Group("translate")]
    [RequireUserPermission(GuildPermission.CreateInstantInvite)]
    public class TranslateModule : ModuleBase<SocketCommandContext>
    {
        [Command, Summary("Translates text to selected language")]
        public async Task Run(string text, string slang, string tlang)
        {
            string api = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={slang}&tl={tlang}&dt=t&q={HttpUtility.UrlEncode(text)}";

            using (WebClient client = new WebClient { Encoding = Encoding.UTF8 })
            {
                try
                {
                    string result = await client.DownloadStringTaskAsync(api);
                    result = result.Substring(4, result.IndexOf('"', 4) - 4);

                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(150, 0, 150)
                        .WithFields(
                            new EmbedFieldBuilder
                            {
                                Name = $"Originální text v jazyce `{slang}`",
                                IsInline = false,
                                Value = text
                            },

                            new EmbedFieldBuilder
                            {
                                Name = $"Přeložený text do jazyku `{tlang}`",
                                IsInline = false,
                                Value = result
                            }
                        ).Build());
                }

                catch (Exception e)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription($"({e.Message})")
                        .WithTitle($"Má databáze nebyla schopna přeložit daný text").Build());
                }
            }
        }
    }
}
