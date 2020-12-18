using System;
using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.Preconditions;
using NOVAxis.Services.Guild;

using Discord;
using Discord.Commands;

using Interactivity;

namespace NOVAxis.Modules.Help
{
    [Cooldown(1)]
    [Group("help"), Alias("?")]
    public class HelpModule : ModuleBase<ShardedCommandContext>
    {
        public InteractivityService InteractivityService { get; set; }
        public GuildService GuildService { get; set; }

        private async Task SendHelp(Embed embed)
        {
            if (Context.User is IGuildUser)
            {
                await Context.Message.DeleteAsync();

                var msg = await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor(Context.User.Username, Context.User.GetAvatarUrl())
                    .WithColor(new Color(52, 231, 231))
                    .WithTitle("Seznam příkazů byl poslán do přímé zprávy").Build());

                InteractivityService.DelayedDeleteMessageAsync(msg, TimeSpan.FromSeconds(5));
            }

            await Context.User.SendMessageAsync(embed: embed);
        }

        [Command, Summary("Shows command list")]
        public async Task ShowHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "**Help**",
                        Value = $"*{prefix}help ___*"
                    },                  

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "**Jisho**",
                        Value = $"*{prefix}help jisho*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "**Clear**",
                        Value = $"*{prefix}help clear*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "**Mute**",
                        Value = $"*{prefix}help mute*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "**Move**",
                        Value = $"*{prefix}help move*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "**Audio**",
                        Value = $"*{prefix}help audio*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = "**MAL**",
                        Value = $"*{prefix}help mal*"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });

            await SendHelp(embed: embed.Build());
        }

        [Command("jisho"), Alias("Jisho"), Summary("Shows command list for Jisho")]
        public async Task ShowJishoHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **jisho**)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = $"**{prefix}jisho** text",
                        Value = "*Prohledá databázi Jisho*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = $"**{prefix}jisho** text limit",
                        Value = "*Prohledá databázi Jisho s omezeným počtem výsledků*"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });


            await SendHelp(embed: embed.Build());
        }

        [Command("move"), Alias("Move"), Summary("Shows command list for Move")]
        public async Task ShowMoveHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **move**)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}move** @user \"channel\"",
                        Value = "*Přesune uživatele do daného kanálu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}move everyone** \"channel\"",
                        Value = "*Přesune všechny uživatele ze současného kanálu do daného kanálu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}move everyone** \"channel1\" \"channel2\"",
                        Value = "*Přesune všechny uživatele z jednoho kanálu do druhého*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}move message** ID \"channel\"",
                        Value = "*Přesune vybranou zprávu z jednoho kanálu do druhého*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}move message** @user \"channel\"",
                        Value = "*Přesune poslední zprávu uživatele do daného kanálu*"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });

            await SendHelp(embed: embed.Build());
        }

        [Command("audio"), Alias("Audio"), Summary("Shows command list for Audio")]
        public async Task ShowAudioHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **audio**)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio join**",
                        Value = "*Připojí se k současnému kanálu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio seek** pozice",
                        Value = "*Posune stopu na danou pozici*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio leave**",
                        Value = "*Odpojí se od připojeného kanálu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio stop**",
                        Value = "*Zastaví stopu a vymaže frontu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio play** URL/název",
                        Value = "*Přehraje vybranou stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio pause**",
                        Value = "*Pozastaví právě hrající stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio status**",
                        Value = "*Zobrazí právě hrající stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio resume**",
                        Value = "*Pokračuje v pozastavené stopě*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio queue**",
                        Value = "*Zobrazí frontu stop*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio skip**",
                        Value = "*Přeskočí stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio remove** pozice",
                        Value = "*Odstraní z fronty vybranou stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio forward** n",
                        Value = "*Posune pozici stopy dopředu o n sekund*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio volume** n",
                        Value = "*Nastaví hlasitost stopy na n procent*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio backward** n",
                        Value = "*Posune pozici stopy dozadu o n sekund*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio setrole** @role/ID",
                        Value = "*Nastaví roli pro identifikaci oprávněných uživatelů*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio repeat**",
                        Value = "*Nastaví režim opakování*"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });


            await SendHelp(embed: embed.Build());
        }

        [Command("clear"), Alias("Clear"), Summary("Shows command list for Clear")]
        public async Task ShowChatHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **clear**)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = $"**{prefix}clear** počet",
                        Value = "*Vymaže určitý počet zpráv z daného kanálu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = $"**{prefix}clear all**",
                        Value = "*Vymaže všechny zprávy z daného kanálu*"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });


            await SendHelp(embed: embed.Build());
        }

        [Command("mal"), Alias("Mal", "MyAnimeList", "myanimelist"), Summary("Shows command list for MAL")]
        public async Task ShowMALHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **mal**)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = $"**{prefix}mal anime** název",
                        Value = "*Prohledá anime databázi MyAnimeList*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = $"**{prefix}mal manga** název",
                        Value = "*Prohledá manga databázi MyAnimeList*"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });


            await SendHelp(embed: embed.Build());
        }

        [Command("mute"), Alias("Mute"), Summary("Shows command list for Mute")]
        public async Task ShowMuteHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **mute**)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}mute** @user",
                        Value = "*Odpojí uživatele od textového kanálu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}mute setrole** @role/ID",
                        Value = "*Nastaví roli pro identifikaci odpojených uživatelů*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}mute setrole**",
                        Value = "*Zruší nastavenou roli*"
                    }
                )
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                });


            await SendHelp(embed: embed.Build());
        }
    }
}
