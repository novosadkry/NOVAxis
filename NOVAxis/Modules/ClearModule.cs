using System;
using System.Linq;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;

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

                var messages = (await Context.Channel.GetMessagesAsync(numberOfMessages).FlattenAsync()).ToList();

                // Removes messages older than 14 days due to Discord API limitations
                messages.RemoveAll(m => DateTime.UtcNow - m.Timestamp.UtcDateTime > TimeSpan.FromDays(14));

                await ((ITextChannel)Context.Channel).DeleteMessagesAsync(messages);

                IMessage message = await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Mé jádro úspěšně vymazalo z existence **{messages.Count}** zpráv" +
                        (messages.Count == 1 ? "u" : Enumerable.Range(1, 4).Contains(messages.Count) ? "y" : "")).Build());

                await Task.Delay(5000);

                try { await message.DeleteAsync(); }
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
