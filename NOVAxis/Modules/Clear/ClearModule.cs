using System;
using System.Linq;
using System.Threading.Tasks;

using NOVAxis.Preconditions;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Modules.Clear
{
    [Cooldown(5)]
    [Group("clear", "Chat-moderation related commands")]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.ManageMessages)]
    public class ClearModule : InteractionModuleBase<ShardedInteractionContext>
    {
        [SlashCommand("this", "Clears a number of the last sent messages")]
        public async Task CmdPurge([MinValue(1), MaxValue(100)] int count)
        {
            if (Context.Channel is not ITextChannel channel)
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Nesprávný typ textového kanálu)")
                    .WithTitle("Mé jádro nebylo schopno vymazat daný počet zpráv").Build());

                return;
            }

            await DeferAsync();

            var messages = (
                from m in await channel.GetMessagesAsync(count).FlattenAsync()
                where DateTimeOffset.Now - m.Timestamp < TimeSpan.FromDays(14)
                select m).ToList();

            var response = await GetOriginalResponseAsync();
            messages.RemoveAll(x => x.Id == response.Id);

            count = messages.Count;
            if (count > 0) await channel.DeleteMessagesAsync(messages);

            await ModifyOriginalResponseAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Mé jádro úspěšně vymazalo z existence **{count}** zpráv" +
                               (count == 1 ? "u" : Enumerable.Range(1, 4).Contains(count) ? "y" : "")).Build();
            });
        }

        [SlashCommand("channel", "Clears a number of the last sent messages")]
        public async Task CmdPurge(ITextChannel channel)
        {
            await DeferAsync();

            var messages = (
                from m in await channel.GetMessagesAsync().FlattenAsync()
                where DateTimeOffset.Now - m.Timestamp < TimeSpan.FromDays(14)
                select m).ToList();

            var response = await GetOriginalResponseAsync();
            messages.RemoveAll(x => x.Id == response.Id);

            int count = messages.Count;
            if (count > 0) await channel.DeleteMessagesAsync(messages);

            await ModifyOriginalResponseAsync(m =>
            {
                m.Embed = new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle($"Mé jádro úspěšně vymazalo z existence **{count}** zpráv" +
                               (count == 1 ? "u" : Enumerable.Range(1, 4).Contains(count) ? "y" : "")).Build();
            });
        }
    }
}
