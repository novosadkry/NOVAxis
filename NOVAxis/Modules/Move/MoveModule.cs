using System;
using System.Linq;
using System.Threading.Tasks;

using NOVAxis.Utilities;
using NOVAxis.Preconditions;

using Discord;
using Discord.WebSocket;
using Discord.Interactions;

namespace NOVAxis.Modules.Move
{
    [Cooldown(2)]
    [Group("move", "Mass moves users between channels")]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.MoveMembers)]
    public class MoveModule : InteractionModuleBase<ShardedInteractionContext>
    {
        public InteractionCache InteractionCache { get; set; }

        [SlashCommand("user", "Moves user to selected channel")]
        public async Task MoveSomeone(IGuildUser user, IVoiceChannel to)
        {
            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Odštěpuji **{user.Username}** a spojuji ho s kanálem `{to.Name}`")
                .Build());

            if (user.VoiceChannel == to)
                return;

            await user.ModifyAsync(u =>
            {
                u.ChannelId = to.Id;
            });
        }

        [Cooldown(5)]
        [SlashCommand("everyone", "Moves everyone from one channel to another")]
        public async Task MoveEveryone(IVoiceChannel to, IVoiceChannel from = null)
        {
            from ??= ((IGuildUser)Context.User).VoiceChannel;

            if (from == null)
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný kanál)")
                    .WithTitle("Mému jádru se nepodařilo získat aktuální kanál")
                    .Build());

                return;
            }

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Odštěpuji **všechny** z kanálu `{from.Name}` a spojuji je s kanálem `{to.Name}`")
                .Build());

            var users = ((SocketGuildChannel)from).Users;

            foreach (IGuildUser u in users)
            {
                if (u.VoiceChannel == to)
                    continue;

                await u.ModifyAsync(prop =>
                {
                    prop.ChannelId = to.Id;
                });
            }
        }

        [MessageCommand("Move message")]
        public async Task MoveMessage(IMessage message)
        {
            InteractionCache[Context.User.Id] = message;

            await RespondAsync(ephemeral: true, embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Do své mezipaměti jsem si uložil tvůj výběr.\n" + 
                    "Pro potvrzení přesunu napiš `/move message kanál`")
                .Build());
        }

        [SlashCommand("message", "Moves a message from one channel to another")]
        public async Task MoveMessage(ITextChannel to)
        {
            var message = InteractionCache[Context.User.Id] as IMessage;

            if (message == null)
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle("Mému jádru se nepodařilo najít zprávu v daném limitu").Build());
            }

            var from = (ITextChannel) message.Channel;

            if (message.Embeds.FirstOrDefault() is Embed embed)
                await to.SendMessageAsync($"Zpráva přesunuta z kanálu #{from}", embed: embed);

            else
            {
                await to.SendMessageAsync($"Zpráva přesunuta z kanálu #{from}", embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithAuthor(message.Author.Username, message.Author.GetAvatarUrl())
                    .WithDescription(message.Content)
                    .WithImageUrl(message.Attachments.FirstOrDefault()?.Url ?? "")
                    .Build());
            }

            await message.DeleteAsync();
        }
    }
}
