using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NOVAxis.Extensions;
using NOVAxis.Web.Hubs;

namespace NOVAxis.Web
{
    /// <summary>
    /// Sends a fresh snapshot into a guild's group. Write endpoints push right after
    /// acting, so the click which caused a change does not wait for the next tick.
    /// </summary>
    public class PlayerBroadcaster
    {
        /// <summary>
        /// The one client-side method the hub ever invokes.
        /// </summary>
        public const string StateMethod = "state";

        private IHubContext<PlayerHub> Hub { get; }
        private PlayerStateService State { get; }

        public PlayerBroadcaster(IHubContext<PlayerHub> hub, PlayerStateService state)
        {
            Hub = hub;
            State = state;
        }

        public Task PushAsync(ulong guildId, CancellationToken cancellationToken = default)
        {
            return Hub.Clients
                .Group(PlayerHub.GroupName(guildId))
                .SendAsync(StateMethod, State.GetState(guildId), cancellationToken);
        }
    }

    /// <summary>
    /// Ticks once a second and pushes state to every guild someone is watching.
    /// Playback moves on many paths - commands, buttons, track transitions, the
    /// inactivity sweep - and a periodic snapshot catches all of them, while the
    /// progress bar needs a regular sample anyway.
    /// </summary>
    public class PlayerBroadcastService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

        private PlayerHubTracker Tracker { get; }
        private PlayerBroadcaster Broadcaster { get; }
        private ILogger<PlayerBroadcastService> Logger { get; }

        public PlayerBroadcastService(
            PlayerHubTracker tracker,
            PlayerBroadcaster broadcaster,
            ILogger<PlayerBroadcastService> logger)
        {
            Tracker = tracker;
            Broadcaster = broadcaster;
            Logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                foreach (var guildId in Tracker.ActiveGuilds)
                {
                    try
                    {
                        await Broadcaster.PushAsync(guildId, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception e)
                    {
                        Logger.Warning($"Unable to push the state of guild {guildId}", e);
                    }
                }
            }
        }
    }
}
