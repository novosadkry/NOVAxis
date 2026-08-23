using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NOVAxis.Core;
using NOVAxis.Extensions;

using Discord.WebSocket;

namespace NOVAxis.Services.Audio.YtDlp
{
    /// <summary>
    /// Disconnects players which have nothing left to play, or which are left alone in their
    /// voice channel. Replaces the inactivity trackers Lavalink4NET provides for the other backend.
    /// </summary>
    public class AudioInactivityTracker : BackgroundService
    {
        /// <summary>
        /// How closely a disconnect can follow the timeout which caused it. A sweep is
        /// only reading cached state, so this is kept short enough not to be noticed.
        /// </summary>
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

        private readonly ConcurrentDictionary<ulong, DateTimeOffset> _aloneSince = new();

        private YtDlpAudioPlayerManager Manager { get; }
        private AudioNotifier Notifier { get; }
        private IOptions<AudioOptions> Options { get; }
        private ILogger<AudioInactivityTracker> Logger { get; }

        public AudioInactivityTracker(
            YtDlpAudioPlayerManager manager,
            AudioNotifier notifier,
            IOptions<AudioOptions> options,
            ILogger<AudioInactivityTracker> logger)
        {
            Manager = manager;
            Notifier = notifier;
            Options = options;
            Logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(PollInterval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await SweepAsync();
                }
                catch (Exception e)
                {
                    Logger.Warning("Inactivity sweep failed", e);
                }
            }
        }

        private async Task SweepAsync()
        {
            var timeout = Options.Value.Timeout;
            var now = DateTimeOffset.UtcNow;
            var players = Manager.ActivePlayers;

            ForgetDeadPlayers(players);

            foreach (var player in players)
            {
                var reason = GetInactivityReason(player, timeout, now);

                if (reason == null)
                {
                    LogPendingTimeout(player, timeout, now);
                    continue;
                }

                Logger.Info($"Disconnecting from guild {player.GuildId}: {reason}");

                _aloneSince.TryRemove(player.GuildId, out _);

                await Notifier.PlayerInactiveAsync(player.TextChannel, player.VoiceChannel.Name);
                await player.DisposeAsync();
            }
        }

        /// <summary>
        /// Drops the timestamps of players which went away between sweeps, as a stale
        /// one would disconnect the guild's next session at once.
        /// </summary>
        private void ForgetDeadPlayers(IReadOnlyCollection<YtDlpAudioPlayer> players)
        {
            var live = players.Select(x => x.GuildId).ToHashSet();

            foreach (var guildId in _aloneSince.Keys)
            {
                if (!live.Contains(guildId))
                    _aloneSince.TryRemove(guildId, out _);
            }
        }

        /// <summary>
        /// Follows the countdown of a player on its way out. A sweep runs every second, so
        /// this belongs in a trace rather than in the log a running bot writes.
        /// </summary>
        private void LogPendingTimeout(YtDlpAudioPlayer player, AudioTimeoutOptions timeout, DateTimeOffset now)
        {
            // The reservation itself is logged where it is made
            if (now < player.ReservedUntil)
                return;

            if (player.InactiveSince is { } inactiveSince && player.Queue.Count == 0)
            {
                Logger.Trace($"Guild {player.GuildId} has had nothing to play for " +
                             $"{Seconds(now - inactiveSince)}s of {Seconds(timeout.IdleInactivity)}s");
            }

            if (_aloneSince.TryGetValue(player.GuildId, out var aloneSince))
            {
                Logger.Trace($"Guild {player.GuildId} has been alone for " +
                             $"{Seconds(now - aloneSince)}s of {Seconds(timeout.UsersInactivity)}s");
            }
        }

        private string GetInactivityReason(YtDlpAudioPlayer player, AudioTimeoutOptions timeout, DateTimeOffset now)
        {
            // A command is still working with it, and has yet to enqueue anything
            if (now < player.ReservedUntil)
                return null;

            if (timeout.IdleInactivity > TimeSpan.Zero &&
                player.State == AudioPlayerState.NotPlaying &&
                player.Queue.Count == 0 &&
                player.InactiveSince is { } inactiveSince &&
                now - inactiveSince >= timeout.IdleInactivity)
                return $"nothing left to play for {Seconds(now - inactiveSince)}s";

            if (timeout.UsersInactivity > TimeSpan.Zero && IsAlone(player))
            {
                var aloneSince = _aloneSince.GetOrAdd(player.GuildId, now);

                // Only equal on the sweep which added it, which is where the countdown starts
                if (aloneSince == now)
                    Logger.Debug($"Guild {player.GuildId} was left alone, " +
                                 $"disconnecting in {Seconds(timeout.UsersInactivity)}s");

                if (now - aloneSince >= timeout.UsersInactivity)
                    return $"no listeners left for {Seconds(now - aloneSince)}s";
            }

            else
            {
                _aloneSince.TryRemove(player.GuildId, out _);
            }

            return null;
        }

        private static string Seconds(TimeSpan value)
        {
            return value.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Uses the gateway's cached voice states - polling this over REST every few seconds
        /// would be needlessly expensive.
        /// </summary>
        private static bool IsAlone(YtDlpAudioPlayer player)
        {
            return player.VoiceChannel is SocketVoiceChannel channel &&
                   channel.ConnectedUsers.All(x => x.IsBot);
        }
    }
}
