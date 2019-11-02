﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

using SharpLink;

namespace NOVAxis.Modules
{
    [Group("move"), Alias("mv")]
    [RequireContext(ContextType.Guild)]
    [RequireUserPermission(GuildPermission.MoveMembers)]
    public class MoveModule : ModuleBase<SocketCommandContext>
    {
        public Services.AudioModuleService AudioModuleService { get; set; }

        [Command, Summary("Moves user to selected channel")]
        public async Task MoveSomeone(IGuildUser user, string channelname)
        {
            try
            {
                IVoiceChannel channel = (from ch in Context.Guild.VoiceChannels
                                         where ch.Name.Contains(channelname)
                                         select ch).Single();

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(150, 0, 150)
                    .WithTitle($"Odštěpuji **{user.Username}** a spojuji ho s kanálem `{channel.Name}`").Build());

                if (user.VoiceChannel == channel)
                    return;

                if (user.Id == Context.Client.CurrentUser.Id)
                {
                    var service = AudioModuleService[Context.Guild.Id];

                    var track = service.CurrentTrack;
                    long trackPos = service.GetPlayer().CurrentPosition;

                    AudioModuleService[Context.Guild.Id].Queue.Insert(0, track);

                    await Services.LavalinkService.Manager.LeaveAsync(Context.Guild.Id);
                    await Services.LavalinkService.Manager.JoinAsync(channel);

                    await service.GetPlayer().SeekAsync((int)trackPos);
                }

                await user.ModifyAsync((GuildUserProperties prop) =>
                {
                    prop.ChannelId = channel.Id;
                });
            }

            catch (InvalidOperationException)
            {
                await Context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný kanál)")
                    .WithTitle($"Má databáze nebyla schopna rozpoznat daný prvek").Build());
            }
        }

        [Command("everyone"), Alias("everybody"), Summary("Moves everyone in your channel to selected channel")]
        public async Task MoveEveryone(string channelname)
        {
            try
            {
                IVoiceChannel channel1 = ((IGuildUser)Context.User).VoiceChannel;

                IVoiceChannel channel2 = (from ch in Context.Guild.VoiceChannels
                                          where ch.Name.Contains(channelname)
                                          select ch).Single();

                if (channel1 == null)
                {
                    await Context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                        .WithColor(220, 20, 60)
                        .WithDescription("(Neplatný kanál)")
                        .WithTitle($"Mému jádru se nepodařilo získat aktuální kanál").Build());

                    return;
                }

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(150, 0, 150)
                    .WithTitle($"Odštěpuji **všechny** a spojuji je s kanálem `{channel2.Name}`").Build());

                foreach (IGuildUser u in ((SocketVoiceChannel)channel1).Users)
                {
                    if (u.VoiceChannel == channel2)
                        continue;

                    if (u.Id == Context.Client.CurrentUser.Id)
                    {
                        var service = AudioModuleService[Context.Guild.Id];

                        var track = service.CurrentTrack;
                        long trackPos = service.GetPlayer().CurrentPosition;

                        AudioModuleService[Context.Guild.Id].Queue.Insert(0, track);

                        await Services.LavalinkService.Manager.LeaveAsync(Context.Guild.Id);
                        await Services.LavalinkService.Manager.JoinAsync(channel2);

                        await service.GetPlayer().SeekAsync((int)trackPos);

                        continue;
                    }

                    await u.ModifyAsync((GuildUserProperties prop) =>
                    {
                        prop.ChannelId = channel2.Id;
                    });
                }
            }

            catch (InvalidOperationException)
            {
                await Context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný kanál)")
                    .WithTitle($"Má databáze nebyla schopna rozpoznat daný prvek").Build());
            }
        }

        [Command("everyone"), Summary("Moves everyone in channel1 to channel2")]
        public async Task MoveEveryoneTo(string channelname1, string channelname2)
        {
            try
            {
                IVoiceChannel channel1 = (from ch in Context.Guild.VoiceChannels
                                          where ch.Name.Contains(channelname1)
                                          select ch).Single();

                IVoiceChannel channel2 = (from ch in Context.Guild.VoiceChannels
                                          where ch.Name.Contains(channelname2)
                                          select ch).Single();

                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(150, 0, 150)
                    .WithTitle($"Odštěpuji **všechny** z kanálu `{channel1.Name}` a spojuji je s kanálem `{channel2.Name}`").Build());

                foreach (IGuildUser u in ((SocketGuildChannel)channel1).Users)
                {
                    if (u.VoiceChannel == channel2)
                        continue;

                    if (u.Id == Context.Client.CurrentUser.Id)
                    {
                        var service = AudioModuleService[Context.Guild.Id];

                        var track = service.CurrentTrack;
                        long trackPos = service.GetPlayer().CurrentPosition;

                        AudioModuleService[Context.Guild.Id].Queue.Insert(0, track);

                        await Services.LavalinkService.Manager.LeaveAsync(Context.Guild.Id);
                        await Services.LavalinkService.Manager.JoinAsync(channel2);

                        await service.GetPlayer().SeekAsync((int)trackPos);

                        continue;
                    }

                    await u.ModifyAsync((GuildUserProperties prop) =>
                    {
                        prop.ChannelId = channel2.Id;
                    });
                }
            }

            catch (InvalidOperationException)
            {
                await Context.Channel.SendMessageAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný kanál)")
                    .WithTitle($"Má databáze nebyla schopna rozpoznat daný prvek").Build());
            }
        }

        [Command("message"), Summary("Moves a message from channel1 to channel2")]
        public async Task MoveMessageTo(ulong id, ITextChannel channel2)
        {
            IMessage msg = await Context.Channel.GetMessageAsync(id);

            await MoveMessageTo(msg, channel2);
        }

        [Command("message"), Summary("Moves a message from channel1 to channel2")]
        public async Task MoveMessageTo(IGuildUser user, ITextChannel channel2, ushort limit = 10)
        {
            IEnumerable<IMessage> messages = await Context.Channel.GetMessagesAsync(limit).FlattenAsync();

            IMessage msg = (from m in messages
                            where m.Author == user
                            orderby m.Timestamp descending
                            select m).FirstOrDefault();

            await MoveMessageTo(msg, channel2);
        }

        private async Task MoveMessageTo(IMessage msg, ITextChannel channel2)
        {
            if (msg == null)
            {
                await ReplyAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithDescription("(Neplatný argument)")
                    .WithTitle($"Mému jádru se nepodařilo najít zprávu v daném limitu").Build());
            }

            ITextChannel channel1 = (ITextChannel)msg.Channel;

            if (msg.Embeds.FirstOrDefault() is Embed embed)
                await channel2.SendMessageAsync($"Zpráva přesunuta z kanálu #{channel1}", 
                    embed: embed);

            else
                await channel2.SendMessageAsync($"Zpráva přesunuta z kanálu #{channel1}", 
                    embed: new EmbedBuilder()
                    .WithColor(150, 0, 150)
                    .WithAuthor(msg.Author.Username, msg.Author.GetAvatarUrl())
                    .WithDescription(msg.Content)
                    .WithImageUrl(msg.Attachments.FirstOrDefault()?.Url ?? "")
                    .Build());

            await msg.DeleteAsync();
        }
    }
}
