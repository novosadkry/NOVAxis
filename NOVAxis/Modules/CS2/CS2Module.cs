using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using Discord;
using Discord.Interactions;

using NOVAxis.Database;
using NOVAxis.Preconditions;
using NOVAxis.Services.CS2;

namespace NOVAxis.Modules.CS2
{
    [Cooldown(5)]
    [RequireContext(ContextType.Guild)]
    [Group("cs2", "Various commands for tracking CS2 match stats")]
    public partial class CS2Module : InteractionModuleBase<ShardedInteractionContext>
    {
        public class PlayerStats
        {
            public string PlayerName { get; set; } = string.Empty;
            public int GamesPlayed { get; set; }
            public double KDRatio { get; set; }
            public double HeadshotPercentage { get; set; }
        }

        public ProgramDbContext DbContext { get; set; }
        public CS2DemoService DemoService { get; set; }
        public CS2DemoQueueService DemoQueueService { get; set; }

        [GeneratedRegex(@"^replay\d+\.valve\.net$")]
        private static partial Regex ValveReplayDomainRegex();

        [SlashCommand("analyze", "Analyzes a CS2 demo file and saves stats to the database")]
        public async Task CmdAnalyze(string demoUrl)
        {
            if (!Uri.IsWellFormedUriString(demoUrl, UriKind.Absolute))
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Neplatná URL adresa demo souboru.")
                    .Build(), ephemeral: true);
                return;
            }

            var regex = ValveReplayDomainRegex();
            var uri = new Uri(demoUrl);

            if (!regex.IsMatch(uri.Host))
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithTitle("Neplatná URL adresa demo souboru. " +
                               "Podporovány jsou pouze demo soubory z replay" +
                               "[serverů Valve](https://steamcommunity.com/my/gcpd/730?tab=matchhistorypremier).")
                    .Build(), ephemeral: true);
                return;
            }

            if (await DemoService.AlreadyProcessed(demoUrl))
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithTitle("Demo již bylo v minulosti zpracováno.")
                    .Build(), ephemeral: true);
                return;
            }

            if (await DemoQueueService.HasPendingDemoAsync(demoUrl))
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(220, 20, 60)
                    .WithTitle("Demo je již ve frontě na zpracování.")
                    .Build(), ephemeral: true);
                return;
            }

            await DemoQueueService.EnqueueAsync(demoUrl);

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle("Demo předáno k analýze.")
                .Build(), ephemeral: true);
        }

        [SlashCommand("top", "Displays the top CS2 players based on tracked stats")]
        public async Task CmdTop()
        {
            var topPlayers = await DbContext.CS2PlayerMatchStats
                .Include(s => s.Player)
                .Where(s => s.Player.DiscordId != null)
                .GroupBy(s => s.PlayerId)
                .Select(g => new PlayerStats
                {
                    PlayerName = g.First().Player.Name,
                    GamesPlayed = g.Count(),
                    KDRatio = g.Sum(s => s.Kills) / (double)Math.Max(g.Sum(s => s.Deaths), 1),
                    HeadshotPercentage = g.Sum(s => s.HeadshotKills) / (double)Math.Max(g.Sum(s => s.Kills), 1) * 100
                })
                .OrderByDescending(p => p.KDRatio)
                .ThenByDescending(p => p.HeadshotPercentage)
                .Take(10)
                .ToListAsync();

            var embed = new EmbedBuilder()
                .WithTitle("Nejlepší CS2 hráči tohoto serveru")
                .WithColor(52, 231, 231);

            for (int i = 0; i < topPlayers.Count; i++)
            {
                var player = topPlayers[i];
                var crownEmoji = new Emoji("👑");

                var fieldName = i switch
                {
                    0 => $"`{i + 1}.` {crownEmoji} **{player.PlayerName}** (Odehráno: {player.GamesPlayed})",
                    < 3 => $"`{i + 1}.` **{player.PlayerName}** (Odehráno: {player.GamesPlayed})",
                    _ => $"`{i + 1}.` {player.PlayerName} (Odehráno: {player.GamesPlayed})"
                };

                var fieldValue = $"K/D: {player.KDRatio:F2}, " +
                                 $"HS%: {player.HeadshotPercentage:F2}%";

                embed.AddField(fieldName, fieldValue);
            }

            await RespondAsync(embed: embed.Build(), ephemeral: false);
        }

        [SlashCommand("link", "Links a Discord account to a CS2 player profile")]
        public async Task CmdLink(string steamId, IGuildUser user = null)
        {
            var cs2Player = await DbContext.CS2Players
                .FirstOrDefaultAsync(p => p.SteamId == steamId);

            if (cs2Player == null)
            {
                await RespondAsync(embed: new EmbedBuilder()
                    .WithColor(52, 231, 231)
                    .WithTitle("Hráč s tímto Steam ID nebyl nalezen.")
                    .Build(), ephemeral: true);
                return;
            }

            var target = user ?? (IGuildUser)Context.User;

            cs2Player.DiscordId = target.Id;
            await DbContext.SaveChangesAsync();

            await RespondAsync(embed: new EmbedBuilder()
                .WithColor(52, 231, 231)
                .WithTitle($"Úspěšně jste propojili účet {target} s hráčem {cs2Player.Name}.")
                .Build(), ephemeral: true);
        }
    }
}
