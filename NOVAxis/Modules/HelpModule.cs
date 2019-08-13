﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace NOVAxis.Modules
{
    [Group("help"), Alias("?")]
    [RequireUserPermission(GuildPermission.CreateInstantInvite)]
    public class HelpModule : ModuleBase<SocketCommandContext>
    {
        private async Task SendHelp(Embed embed)
        {
            await Context.Message.DeleteAsync();

            IMessage _message = await ReplyAsync(embed: new EmbedBuilder()
                .WithColor(150, 0, 150)
                .WithTitle($"Seznam příkazů byl úspěšně poslán do přímé zprávy").Build());

            await Context.User.SendMessageAsync(embed: embed);

            await Task.Delay(5000);

            try { await _message.DeleteAsync(); }
            catch (Discord.Net.HttpException) { }
        }

        [Command, Summary("Shows command list")]
        public async Task ShowHelp()
        {
            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Příkaz `Help`",
                        Value = "(Použití -> `~help commandName`)"
                    },
                    
                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Příkaz `Translate`",
                        Value = "(Použití -> `~translate \"text\" \"slang\" \"tlang\"`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Příkaz `Jisho`",
                        Value = "(Nápověda -> `~help jisho`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Příkaz `Clear`",
                        Value = "(Nápověda -> `~help clear`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Příkaz `Mute`",
                        Value = "(Použití -> `~mute @user`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Příkaz `Move`",
                        Value = "(Nápověda -> `~help move`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Příkaz `Audio`",
                        Value = "(Nápověda -> `~help audio`)"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = "https://cdn.discordapp.com/avatars/269182357704015873/3a88a302762e87d012e02665674cfe58.webp?size=1024",
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });

            
            await ReplyAsync(embed: embed.Build());
        }

        [Command("jisho"), Alias("Jisho"), Summary("Shows command list for Jisho")]
        public async Task ShowJishoHelp()
        {
            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro `~jisho`)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Použití -> `~jisho \"text\"`",
                        Value = "(Prohledá databázi Jisho s limitem sto prvků)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Použití -> `~jisho \"text\" LIMIT`",
                        Value = "(Prohledá databázi Jisho s nastaveným limitem prvků)"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = "https://cdn.discordapp.com/avatars/269182357704015873/3a88a302762e87d012e02665674cfe58.webp?size=1024",
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });


            await ReplyAsync(embed: embed.Build());
        }

        [Command("move"), Alias("Move"), Summary("Shows command list for Move")]
        public async Task ShowMoveHelp()
        {
            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro `~move`)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~move @user \"channel\"`",
                        Value = "(Přesune uživatele `user` do `channel1`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~move everyone \"channel\"`",
                        Value = "(Přesune všechny uživatele ze současného kanálu do `channel1`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~move everyone \"channel1\" \"channel2\"`",
                        Value = "(Přesune všechny uživatele z `channel1` do `channel2`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~move message ID \"channel\"`",
                        Value = "(Přesune vybranou zprávu z `channel1` do `channel2`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~move message @user \"channel\" LIMIT`",
                        Value = "(Přesune poslední zprávu uživatele `@user` z `channel1` do `channel2`)"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = "https://cdn.discordapp.com/avatars/269182357704015873/3a88a302762e87d012e02665674cfe58.webp?size=1024",
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });


            await ReplyAsync(embed: embed.Build());
        }

        [Command("audio"), Alias("Audio"), Summary("Shows command list for Audio")]
        public async Task ShowAudioHelp()
        {
            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro `~audio`)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio join`",
                        Value = "(Připojí jádro k současnému kanálu)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio stop`",
                        Value = "(Zastaví aktivní zvukovou stopu)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio leave`",
                        Value = "(Odpojí jádro od připojeného kanálu)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio pause`",
                        Value = "(Pozastaví aktivní zvukovou stopu)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio play URL/Title`",
                        Value = "(Přehraje vybranou zvukovou stopu)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio resume`",
                        Value = "(Spustí pozastavenou zvukovou stopu)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio queue`",
                        Value = "(Zobrazí frontu zvukových stop)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio skip`",
                        Value = "(Přeskočí aktivní zvukovou stopu)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio remove INDEX`",
                        Value = "(Odstraní z fronty vybranou stopu)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio forward SECONDS`",
                        Value = "(Posune pozici stopy dopředu o `seconds`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio volume VALUE`",
                        Value = "(Nastaví hlasitost stopy na `value`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio backward SECONDS`",
                        Value = "(Posune pozici stopy dozadu o `seconds`)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "Použití -> `~audio seek SECONDS`",
                        Value = "(Nastaví pozici stopy na `seconds`)"
                    }     
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = "https://cdn.discordapp.com/avatars/269182357704015873/3a88a302762e87d012e02665674cfe58.webp?size=1024",
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });


            await ReplyAsync(embed: embed.Build());
        }

        [Command("clear"), Alias("Clear"), Summary("Shows command list for Clear")]
        public async Task ShowChatHelp()
        {
            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro `~clear`)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Použití -> `~clear X`",
                        Value = "(Vymaže posledních `X` zpráv z daného kanálu)"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = "Použití -> `~clear all`",
                        Value = "(Vymaže všechny zprávy z daného kanálu)"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = "https://cdn.discordapp.com/avatars/269182357704015873/3a88a302762e87d012e02665674cfe58.webp?size=1024",
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });


            await ReplyAsync(embed: embed.Build());
        }
    }
}
