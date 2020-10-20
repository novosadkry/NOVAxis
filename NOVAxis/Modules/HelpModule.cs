using System;
using System.Threading.Tasks;

using NOVAxis.Services;

using Discord;
using Discord.Commands;

namespace NOVAxis.Modules
{
    [Group("help"), Alias("?")]
    public class HelpModule : ModuleBase<SocketCommandContext>
    {
        public PrefixService PrefixService { get; set; }

        private async Task SendHelp(Embed embed)
        {
            if (Context.User is IGuildUser)
            {
                await Context.Message.DeleteAsync();

                IMessage message = await ReplyAsync(embed: new EmbedBuilder()
                    .WithAuthor(Context.User.Username, Context.User.GetAvatarUrl())
                    .WithColor(new Color(52, 231, 231))
                    .WithTitle($"Seznam příkazů byl úspěšně poslán do přímé zprávy").Build());

                await Context.User.SendMessageAsync(embed: embed);

                await Task.Delay(5000);

                try { await message.DeleteAsync(); }
                catch (Discord.Net.HttpException) { }
            }

            else
                await Context.User.SendMessageAsync(embed: embed);
        }

        [Command, Summary("Shows command list")]
        public async Task ShowHelp()
        {
            string prefix = await PrefixService.GetPrefix(Context);

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
                        Value = $"*{prefix}mute @user*"
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
            string prefix = await PrefixService.GetPrefix(Context);

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
                        Value = "*Prohledá databázi Jisho s limitem sto prvků*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = $"**{prefix}jisho** text limit",
                        Value = "*Prohledá databázi Jisho s nastaveným limitem prvků*"
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
            string prefix = await PrefixService.GetPrefix(Context);

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
                        Value = "*Přesune uživatele user do channel*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}move everyone** \"channel\"",
                        Value = "*Přesune všechny uživatele ze současného kanálu do channel*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}move everyone** \"channel1\" \"channel2\"",
                        Value = "*Přesune všechny uživatele z channel1 do channel2*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}move message** ID \"channel\"",
                        Value = "*Přesune vybranou zprávu z channel1 do channel2*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}move message** @user \"channel\"",
                        Value = "*Přesune poslední zprávu uživatele user do channel*"
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
            string prefix = await PrefixService.GetPrefix(Context);

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
                        Value = "*Připojí jádro k současnému kanálu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio seek** pozice",
                        Value = "*Nastaví novou pozici aktivní zvukové stopy*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio leave**",
                        Value = "*Odpojí jádro od připojeného kanálu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio stop**",
                        Value = "*Zastaví aktivní zvukovou stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio play** URL/název",
                        Value = "*Přehraje vybranou zvukovou stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio pause**",
                        Value = "*Pozastaví aktivní zvukovou stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio status**",
                        Value = "*Zobrazí aktivní zvukovou stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio resume**",
                        Value = "*Spustí pozastavenou zvukovou stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio queue**",
                        Value = "*Zobrazí frontu zvukových stop*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio skip**",
                        Value = "*Přeskočí aktivní zvukovou stopu*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = true,
                        Name = $"**{prefix}audio remove**",
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
            string prefix = await PrefixService.GetPrefix(Context);

            EmbedBuilder embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **clear**)")
                .WithFields(
                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = $"**{prefix}clear** x",
                        Value = "*Vymaže posledních x zpráv z daného kanálu*"
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
        public async Task ShowMalHelp()
        {
            string prefix = await PrefixService.GetPrefix(Context);

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
                        Value = "*Prohledá databázi MyAnimeList*"
                    },

                    new EmbedFieldBuilder
                    {
                        IsInline = false,
                        Name = $"**{prefix}mal manga** název",
                        Value = "*Prohledá databázi MyAnimeList*"
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
