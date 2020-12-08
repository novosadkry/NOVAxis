using System;
using System.Linq;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;

using Interactivity;

namespace NOVAxis.Modules.Clear
{
    [Group("clear")]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public class ClearModule : ModuleBase<SocketCommandContext>
    {
        public InteractivityService InteractivityService { get; set; }

        [Command, Summary("Clears a number of the last sent messages")]
        public async Task Purge(int numberOfMessages)
        {
            try
            {
                if (numberOfMessages > 100)
                {
                    await ReplyAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Maximální povolený počet zpráv je 100)")
                        .WithTitle("Mé jádro nebylo schopno vymazat daný počet zpráv").Build());

                    return;
                }

                var messages = (
                    from m in await Context.Channel.GetMessagesAsync(numberOfMessages + 1).FlattenAsync() // Add 1 to also count the user's invocation message
                    where DateTime.UtcNow - m.Timestamp.UtcDateTime < TimeSpan.FromDays(14)                           // Removes messages older than 14 days due to Discord API limitations
                    select m).ToList();

                await ((ITextChannel)Context.Channel).DeleteMessagesAsync(messages);

                int count = messages.Count - 1;
                var msg = await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Mé jádro úspěšně vymazalo z existence **{count}** zpráv" +
                               (count == 1 ? "u" : Enumerable.Range(1, 4).Contains(count) ? "y" : "")).Build());

                InteractivityService.DelayedDeleteMessageAsync(msg, TimeSpan.FromSeconds(5));
            }

            catch (Exception e)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription($"({e.Message})")
                    .WithTitle("Mé jádro nebylo schopno vymazat daný počet zpráv").Build());
            }
        }

        [Command("all"), Summary("Clears all of the last sent messages")]
        public async Task Purge() => await Purge(100);
    }
}
