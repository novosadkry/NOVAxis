using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace NOVAxis.Modules
{
    [Group("clear")]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public class ClearModule : ModuleBase<SocketCommandContext>
    {
        [Command, Summary("Clears a number of the last sent messages")]
        public async Task Purge(int numberOfMessages)
        {
            try
            {
                await Context.Message.DeleteAsync();

                IEnumerable<IMessage> messages = await Context.Channel.GetMessagesAsync(numberOfMessages).FlattenAsync();

                messages = messages.Where((x) =>
                {
                    return DateTime.UtcNow - x.Timestamp.UtcDateTime <= TimeSpan.FromDays(14);
                });

                int _messagesCount = messages.Count();

                await ((ITextChannel)Context.Channel).DeleteMessagesAsync(messages);

                IMessage _message = await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(150, 0, 150)
                    .WithTitle($"Mé jádro úspěšně vymazalo z existence **{_messagesCount}** zpráv" +
                        ((_messagesCount == 1) ? "u" : (Enumerable.Range(1, 4).Contains(_messagesCount) ? "y" : ""))).Build());

                await Task.Delay(5000);

                try { await _message.DeleteAsync(); }
                catch (Discord.Net.HttpException) { }
            }

            catch (Exception e)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"({e.Message})")
                    .WithTitle($"Mé jádro nebylo schopno vyzamat daný počet zpráv").Build());
            }
        }

        [Command("all"), Summary("Clears all of the last sent messages")]
        public async Task Purge() => await Purge(100);
    }
}
