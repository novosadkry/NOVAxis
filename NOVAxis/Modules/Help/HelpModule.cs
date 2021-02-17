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

            Embed embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů)")

                .AddField("**Help**", $"*{prefix}help ___*", true)
                .AddField("**Jisho**", $"*{prefix}help jisho*", true)
                .AddField("**Clear**", $"*{prefix}help clear*", true)
                .AddField("**Mute**", $"*{prefix}help mute*", true)
                .AddField("**Move**", $"*{prefix}help move*", true)
                .AddField("**Audio**", $"*{prefix}help audio*", true)
                .AddField("**MAL**", $"*{prefix}help mal*", true)

                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await SendHelp(embed);
        }

        [Command("jisho"), Alias("Jisho"), Summary("Shows command list for Jisho")]
        public async Task ShowJishoHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            Embed embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **jisho**)")

                .AddField($"**{prefix}jisho** text", "*Prohledá databázi Jisho*")
                .AddField($"**{prefix}jisho** text limit", "*Prohledá databázi Jisho s omezeným počtem výsledků*")

                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await SendHelp(embed);
        }

        [Command("move"), Alias("Move"), Summary("Shows command list for Move")]
        public async Task ShowMoveHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            Embed embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **move**)")

                .AddField($"**{prefix}move** @user \"channel\"", "*Přesune uživatele do daného kanálu*", true)
                .AddField($"**{prefix}move everyone** \"channel\"", "*Přesune všechny uživatele ze současného kanálu do daného kanálu*", true)
                .AddField($"**{prefix}move everyone** \"channel1\" \"channel2\"", "*Přesune všechny uživatele z jednoho kanálu do druhého*", true)
                .AddField($"**{prefix}move message** ID \"channel\"", "*Přesune vybranou zprávu z jednoho kanálu do druhého*", true)
                .AddField($"**{prefix}move message** @user \"channel\"", "*Přesune poslední zprávu uživatele do daného kanálu*", true)

                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await SendHelp(embed);
        }

        [Command("audio"), Alias("Audio"), Summary("Shows command list for Audio")]
        public async Task ShowAudioHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            Embed embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **audio**)")

                .AddField($"**{prefix}audio join**", "*Připojí se k současnému kanálu*", true)
                .AddField($"**{prefix}audio seek** pozice", "*Posune stopu na danou pozici*", true)
                .AddField($"**{prefix}audio leave**", "*Odpojí se od připojeného kanálu*", true)
                .AddField($"**{prefix}audio stop**", "*Zastaví stopu a vymaže frontu*", true)
                .AddField($"**{prefix}audio play** URL/název", "*Přehraje vybranou stopu*", true)
                .AddField($"**{prefix}audio pause**", "*Pozastaví právě hrající stopu*", true)
                .AddField($"**{prefix}audio status**", "*Zobrazí právě hrající stopu*", true)
                .AddField($"**{prefix}audio resume**", "*Pokračuje v pozastavené stopě*", true)
                .AddField($"**{prefix}audio queue**", "*Zobrazí frontu stop*", true)
                .AddField($"**{prefix}audio skip**", "*Přeskočí stopu*", true)
                .AddField($"**{prefix}audio remove** pozice", "*Odstraní z fronty vybranou stopu*", true)
                .AddField($"**{prefix}audio forward** n", "*Posune pozici stopy dopředu o n sekund*", true)
                .AddField($"**{prefix}audio volume** n", "*Nastaví hlasitost stopy na n procent*", true)
                .AddField($"**{prefix}audio backward** n", "*Posune pozici stopy dozadu o n sekund*", true)
                .AddField($"**{prefix}audio setrole** @role/ID", "*Nastaví roli pro identifikaci oprávněných uživatelů*", true)
                .AddField($"**{prefix}audio repeat**", "*Nastaví režim opakování*", true)

                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await SendHelp(embed);
        }

        [Command("clear"), Alias("Clear"), Summary("Shows command list for Clear")]
        public async Task ShowChatHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            Embed embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **clear**)")

                .AddField($"**{prefix}clear** počet", "*Vymaže určitý počet zpráv z daného kanálu*")
                .AddField($"**{prefix}clear all**", "*Vymaže všechny zprávy z daného kanálu*")

                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await SendHelp(embed);
        }

        [Command("mal"), Alias("Mal", "MyAnimeList", "myanimelist"), Summary("Shows command list for MAL")]
        public async Task ShowMALHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            Embed embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **mal**)")

                .AddField($"**{prefix}mal anime** název", "*Prohledá anime databázi MyAnimeList*")
                .AddField($"**{prefix}mal manga** název", "*Prohledá manga databázi MyAnimeList*")

                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await SendHelp(embed);
        }

        [Command("mute"), Alias("Mute"), Summary("Shows command list for Mute")]
        public async Task ShowMuteHelp()
        {
            var guildInfo = await GuildService.GetInfo(Context);
            string prefix = guildInfo.Prefix;

            Embed embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **mute**)")

                .AddField($"**{prefix}mute** @user", "*Odpojí uživatele od textového kanálu*", true)
                .AddField($"**{prefix}mute setrole** @role/ID", "*Nastaví roli pro identifikaci odpojených uživatelů*", true)
                .AddField($"**{prefix}mute setrole**", "*Zruší nastavenou roli*", true)

                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await SendHelp(embed);
        }
    }
}
