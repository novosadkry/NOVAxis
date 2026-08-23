using System;
using System.Collections.Concurrent;
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
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

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

            // A player can go away without the sweep noticing - /leave, a dropped connection -
            // and a timestamp left behind would disconnect the guild's next session at once
            var live = players.Select(x => x.GuildId).ToHashSet();

            foreach (var guildId in _aloneSince.Keys)
            {
                if (!live.Contains(guildId))
                    _aloneSince.TryRemove(guildId, out _);
            }

            foreach (var player in players)
            {
                var reason = GetInactivityReason(player, timeout, now);

                if (reason == null)
                    continue;

                Logger.Info($"Disconnecting from guild {player.GuildId}: {reason}");

                _aloneSince.TryRemove(player.GuildId, out _);

                await Notifier.PlayerInactiveAsync(player.TextChannel, player.VoiceChannel.Name);
                await player.DisposeAsync();
            }
        }

        private string GetInactivityReason(YtDlpAudioPlayer player, AudioTimeoutOptions timeout, DateTimeOffset now)
        {
            // A command is still working with it, and its track has not reached the queue yet
            if (now < player.ReservedUntil)
                return null;

            if (timeout.IdleInactivity > TimeSpan.Zero &&
                player.State == AudioPlayerState.NotPlaying &&
                player.Queue.Count == 0 &&
                player.InactiveSince is { } inactiveSince &&
                now - inactiveSince >= timeout.IdleInactivity)
                return "nothing left to play";

            if (timeout.UsersInactivity > TimeSpan.Zero && IsAlone(player))
            {
                var aloneSince = _aloneSince.GetOrAdd(player.GuildId, now);

                if (now - aloneSince >= timeout.UsersInactivity)
                    return "no listeners left";
            }

            else
            {
                _aloneSince.TryRemove(player.GuildId, out _);
            }

            return null;
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
