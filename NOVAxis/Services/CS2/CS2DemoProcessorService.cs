using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DemoFile;
using DemoFile.Game.Cs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NOVAxis.Database;
using NOVAxis.Database.Entities;
using NOVAxis.Extensions;

namespace NOVAxis.Services.CS2
{
    public class CS2DemoProcessorService
    {
        private readonly ProgramDbContext _dbContext;
        private readonly ILogger<CS2DemoProcessorService> _logger;

        public CS2DemoProcessorService(
            ProgramDbContext dbContext,
            ILogger<CS2DemoProcessorService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task ProcessDemoAsync(string demoUrl, string demoFilePath)
        {
            try
            {
                var demo = new CsDemoParser();

                // Store player stats during demo parsing
                var playerStats = new Dictionary<ulong, PlayerDemoStats>();
                var matchInfo = new MatchInfo();

                // Subscribe to events
                demo.Source1GameEvents.PlayerDeath += e =>
                {
                    if (e.Attacker == null || e.Player == null) return;

                    var attackerSteamId = e.Attacker.SteamID;
                    var victimSteamId = e.Player.SteamID;

                    if (!playerStats.ContainsKey(attackerSteamId))
                        InitializePlayerStats(playerStats, e.Attacker);
                    if (!playerStats.ContainsKey(victimSteamId))
                        InitializePlayerStats(playerStats, e.Player);

                    playerStats[attackerSteamId].Kills++;
                    playerStats[victimSteamId].Deaths++;

                    if (e.Headshot)
                        playerStats[attackerSteamId].HeadshotKills++;

                    if (e.Assister != null)
                    {
                        var assisterSteamId = e.Assister.SteamID;
                        if (!playerStats.ContainsKey(assisterSteamId))
                            InitializePlayerStats(playerStats, e.Assister);
                        playerStats[assisterSteamId].Assists++;
                    }
                };

                demo.Source1GameEvents.RoundMvp += e =>
                {
                    if (e.Player == null) return;
                    var steamId = e.Player.SteamID;
                    if (!playerStats.ContainsKey(steamId))
                        InitializePlayerStats(playerStats, e.Player);
                    playerStats[steamId].MVPs++;
                };

                // Capture tick interval from server info
                var tickInterval = 64.0f;
                demo.PacketEvents.SvcServerInfo += e =>
                {
                    tickInterval = e.TickInterval;
                };

                // Parse the demo
                await using var fileStream = File.OpenRead(demoFilePath);
                var reader = DemoFileReader.Create(demo, fileStream);
                await reader.ReadAllAsync();

                // Get match info from demo header
                matchInfo.DemoUrl = demoUrl;
                matchInfo.MapName = demo.FileHeader?.MapName ?? "Unknown map";
                matchInfo.DurationSeconds = (int)(demo.CurrentDemoTick.Value / tickInterval);
                matchInfo.MatchDate = DateTime.UtcNow;

                // Convert playerStats dictionary to use string keys
                var playerStatsString = playerStats.ToDictionary(
                    kvp => kvp.Key.ToString(),
                    kvp => new PlayerDemoStats
                    {
                        Name = kvp.Value.Name,
                        Team = kvp.Value.Team,
                        Kills = kvp.Value.Kills,
                        Deaths = kvp.Value.Deaths,
                        Assists = kvp.Value.Assists,
                        HeadshotKills = kvp.Value.HeadshotKills,
                        MVPs = kvp.Value.MVPs
                    });

                // Save to database
                await SaveMatchDataAsync(matchInfo, playerStatsString);

            }
            catch (Exception ex)
            {
                _logger.Error($"Error processing demo file: {demoFilePath}", ex);
                throw;
            }
        }

        private void InitializePlayerStats(Dictionary<ulong, PlayerDemoStats> playerStats, CCSPlayerController? player)
        {
            if (player == null) return;

            playerStats[player.SteamID] = new PlayerDemoStats
            {
                Name = player.PlayerName,
                Team = player.Team.ToString()
            };
        }

        private async Task SaveMatchDataAsync(MatchInfo matchInfo, Dictionary<string, PlayerDemoStats> playerStats)
        {
            // Create match record
            var match = new CS2Match
            {
                MapName = matchInfo.MapName,
                MatchDate = matchInfo.MatchDate,
                DemoUrl = matchInfo.DemoUrl,
                DurationSeconds = matchInfo.DurationSeconds,
                ProcessedAt = DateTime.UtcNow
            };

            _dbContext.CS2Matches.Add(match);

            // Process each player
            foreach (var (steamId, stats) in playerStats)
            {
                // Find or create player
                var player = await _dbContext.CS2Players
                    .FirstOrDefaultAsync(p => p.SteamId == steamId);

                if (player == null)
                {
                    player = new CS2Player
                    {
                        SteamId = steamId,
                        Name = stats.Name,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    _dbContext.CS2Players.Add(player);
                }
                else
                {
                    player.Name = stats.Name; // Update name if changed
                    player.UpdatedAt = DateTime.UtcNow;
                }

                // Create player match stats
                var playerMatchStats = new CS2PlayerMatchStats
                {
                    Match = match,
                    Player = player,
                    Kills = stats.Kills,
                    Deaths = stats.Deaths,
                    Assists = stats.Assists,
                    HeadshotKills = stats.HeadshotKills,
                    Score = stats.Kills * 2 + stats.Assists,
                    MVPs = stats.MVPs,
                    Team = stats.Team
                };

                _dbContext.CS2PlayerMatchStats.Add(playerMatchStats);
            }

            await _dbContext.SaveChangesAsync();
        }

        private class PlayerDemoStats
        {
            public string Name { get; set; }
            public string Team { get; set; }
            public int Kills { get; set; }
            public int Deaths { get; set; }
            public int Assists { get; set; }
            public int HeadshotKills { get; set; }
            public int MVPs { get; set; }
        }

        private class MatchInfo
        {
            public string MapName { get; set; }
            public string DemoUrl { get; set; }
            public int DurationSeconds { get; set; }
            public DateTime MatchDate { get; set; }
        }
    }
}
