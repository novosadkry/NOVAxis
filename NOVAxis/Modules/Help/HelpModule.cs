using System;
using System.Threading.Tasks;

using NOVAxis.Core;
using NOVAxis.Preconditions;

using Discord;
using Discord.Interactions;

namespace NOVAxis.Modules.Help
{
    [Cooldown(1)]
    [Group("help", "Shows documentation for certain commands")]
    public class HelpModule : InteractionModuleBase<ShardedInteractionContext>
    {
        [SlashCommand("list", "Shows command list")]
        public async Task CmdShowHelp()
        {
            var embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů)")
                .AddField("**Help**", "*/help ___*", true)
                .AddField("**Jisho**", "*/help jisho*", true)
                .AddField("**Clear**", "*/help clear*", true)
                .AddField("**Mute**", "*/help mute*", true)
                .AddField("**Move**", "*/help move*", true)
                .AddField("**Audio**", "*/help audio*", true)
                .AddField("**MAL**", "*/help mal*", true)
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await RespondAsync(ephemeral: true, embed: embed);
        }

        [SlashCommand("jisho", "Shows command list for Jisho")]
        public async Task CmdShowJishoHelp()
        {
            var embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **jisho**)")
                .AddField("**/jisho** text", "*Prohledá databázi Jisho*")
                .AddField("**/jisho** text limit", "*Prohledá databázi Jisho s omezeným počtem výsledků*")
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await RespondAsync(ephemeral: true, embed: embed);
        }

        [SlashCommand("move", "Shows command list for Move")]
        public async Task CmdShowMoveHelp()
        {
            var embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **move**)")
                .AddField("**/move** @user \"channel\"", "*Přesune uživatele do daného kanálu*", true)
                .AddField("**/move everyone** \"channel\"", "*Přesune všechny uživatele ze současného kanálu do daného kanálu*", true)
                .AddField("**/move everyone** \"channel1\" \"channel2\"", "*Přesune všechny uživatele z jednoho kanálu do druhého*", true)
                .AddField("**/move message** ID \"channel\"", "*Přesune vybranou zprávu z jednoho kanálu do druhého*", true)
                .AddField("**/move message** @user \"channel\"", "*Přesune poslední zprávu uživatele do daného kanálu*", true)
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await RespondAsync(ephemeral: true, embed: embed);
        }

        [SlashCommand("audio", "Shows command list for Audio")]
        public async Task CmdShowAudioHelp()
        {
            var embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **audio**)")
                .AddField("**/audio join**", "*Připojí se k současnému kanálu*", true)
                .AddField("**/audio seek** pozice", "*Posune stopu na danou pozici*", true)
                .AddField("**/audio leave**", "*Odpojí se od připojeného kanálu*", true)
                .AddField("**/audio stop**", "*Zastaví stopu a vymaže frontu*", true)
                .AddField("**/audio play** URL/název", "*Přehraje vybranou stopu*", true)
                .AddField("**/audio pause**", "*Pozastaví právě hrající stopu*", true)
                .AddField("**/audio status**", "*Zobrazí právě hrající stopu*", true)
                .AddField("**/audio resume**", "*Pokračuje v pozastavené stopě*", true)
                .AddField("**/audio queue**", "*Zobrazí frontu stop*", true)
                .AddField("**/audio skip**", "*Přeskočí stopu*", true)
                .AddField("**/audio remove** pozice", "*Odstraní z fronty vybranou stopu*", true)
                .AddField("**/audio forward** n", "*Posune pozici stopy dopředu o n sekund*", true)
                .AddField("**/audio volume** n", "*Nastaví hlasitost stopy na n procent*", true)
                .AddField("**/audio backward** n", "*Posune pozici stopy dozadu o n sekund*", true)
                .AddField("**/audio setrole** @role/ID", "*Nastaví roli pro identifikaci oprávněných uživatelů*", true)
                .AddField("**/audio repeat**", "*Nastaví režim opakování*", true)
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await RespondAsync(ephemeral: true, embed: embed);
        }

        [SlashCommand("clear", "Shows command list for Clear")]
        public async Task CmdShowChatHelp()
        {
            var embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **clear**)")
                .AddField("**/clear** počet", "*Vymaže určitý počet zpráv z daného kanálu*")
                .AddField("**/clear all**", "*Vymaže všechny zprávy z daného kanálu*")
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await RespondAsync(ephemeral: true, embed: embed);
        }

        [SlashCommand("mal", "Shows command list for MAL")]
        public async Task CmdShowMALHelp()
        {
            var embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **mal**)")
                .AddField("**/mal anime** název", "*Prohledá anime databázi MyAnimeList*")
                .AddField("**/mal manga** název", "*Prohledá manga databázi MyAnimeList*")
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await RespondAsync(ephemeral: true, embed: embed);
        }

        [SlashCommand("mute", "Shows command list for Mute")]
        public async Task CmdShowMuteHelp()
        {
            var embed = new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder { Name = "NOVAxis", IconUrl = Context.Client.CurrentUser.GetAvatarUrl() })
                .WithColor(new Color(52, 231, 231))
                .WithTitle("Příručka pro telekomunikaci s jádrem NOVAxis")
                .WithDescription("(Seznam příkazů pro **mute**)")
                .AddField("**/mute** @user", "*Odpojí uživatele od textového kanálu*", true)
                .AddField("**/mute setrole** @role/ID", "*Nastaví roli pro identifikaci odpojených uživatelů*", true)
                .AddField("**/mute setrole**", "*Zruší nastavenou roli*", true)
                .WithFooter(new EmbedFooterBuilder
                {
                    IconUrl = Context.Client.GetUser(Program.OwnerId).GetAvatarUrl(),
                    Text = $"© Kryštof Novosad | {DateTime.Now}"
                })
                .Build();

            await RespondAsync(ephemeral: true, embed: embed);
        }
    }
}
