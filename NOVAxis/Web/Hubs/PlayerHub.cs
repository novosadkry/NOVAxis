using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

using NOVAxis.Web.Auth;
using NOVAxis.Web.Contracts;

namespace NOVAxis.Web.Hubs
{
    /// <summary>
    /// Streams player state to browsers. A connection subscribes to the guilds it
    /// watches and every subscription is gated on the caller's membership.
    /// </summary>
    [Authorize]
    public class PlayerHub : Hub
    {
        private GuildAccessService Access { get; }
        private PlayerStateService State { get; }
        private PlayerHubTracker Tracker { get; }

        public PlayerHub(GuildAccessService access, PlayerStateService state, PlayerHubTracker tracker)
        {
            Access = access;
            State = state;
            Tracker = tracker;
        }

        public static string GroupName(ulong guildId) => $"guild:{guildId}";

        public async Task<PlayerStateDto> Subscribe(string guildId)
        {
            var id = Parse(guildId);
            var user = await Access.GetGuildUserAsync(id, Context.User.GetDiscordId());

            if (user == null)
                throw new HubException("Nejste členem tohoto serveru");

            Tracker.Add(Context.ConnectionId, id);
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(id));

            return State.GetState(id);
        }

        /// <summary>
        /// Says whether this connection currently wants the spectrum for a guild it is
        /// already watching. A page with the visualiser off screen - collapsed, or in a
        /// background tab - should not be sent thirty frames a second it will never draw.
        /// </summary>
        public Task SetSpectrum(string guildId, bool wanted)
        {
            Tracker.Spectrum(Context.ConnectionId, Parse(guildId), wanted);
            return Task.CompletedTask;
        }

        public async Task Unsubscribe(string guildId)
        {
            var id = Parse(guildId);

            Tracker.Remove(Context.ConnectionId, id);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(id));
        }

        public override Task OnDisconnectedAsync(Exception exception)
        {
            Tracker.Drop(Context.ConnectionId);
            return base.OnDisconnectedAsync(exception);
        }

        private static ulong Parse(string guildId)
        {
            return ulong.TryParse(guildId, out var id)
                ? id
                : throw new HubException("Neplatné id serveru");
        }
    }

    /// <summary>
    /// Remembers which guilds have a browser watching, because SignalR does not tell
    /// which groups are non-empty and ticking for nobody would be wasted work.
    /// </summary>
    public sealed class PlayerHubTracker
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, HashSet<ulong>> _connections = new();
        private readonly Dictionary<ulong, int> _watchers = new();

        /// <summary>
        /// The same again, for the far more expensive thing. Kept here beside the watchers
        /// rather than in a tracker of its own: which connection is looking at what is one
        /// truth, and splitting it in two is how the halves come to disagree.
        /// </summary>
        private readonly Dictionary<string, HashSet<ulong>> _spectrumConnections = new();
        private readonly Dictionary<ulong, int> _spectrumWatchers = new();

        public IReadOnlyList<ulong> ActiveGuilds
        {
            get { lock (_sync) return _watchers.Keys.ToList(); }
        }

        /// <summary>Guilds somebody has the spectrum open for.</summary>
        public IReadOnlyList<ulong> SpectrumGuilds
        {
            get { lock (_sync) return _spectrumWatchers.Keys.ToList(); }
        }

        public void Spectrum(string connectionId, ulong guildId, bool wanted)
        {
            lock (_sync)
            {
                // Only for a guild this connection is already watching - the subscription
                // is where membership was checked, and this must not be a way around it
                if (!_connections.TryGetValue(connectionId, out var watching) ||
                    !watching.Contains(guildId))
                    return;

                var guilds = _spectrumConnections.TryGetValue(connectionId, out var existing)
                    ? existing
                    : _spectrumConnections[connectionId] = new HashSet<ulong>();

                if (wanted)
                {
                    if (guilds.Add(guildId))
                        _spectrumWatchers[guildId] = _spectrumWatchers.GetValueOrDefault(guildId) + 1;
                }
                else if (guilds.Remove(guildId))
                {
                    ReleaseSpectrum(guildId);
                }
            }
        }

        public void Add(string connectionId, ulong guildId)
        {
            lock (_sync)
            {
                var guilds = _connections.TryGetValue(connectionId, out var existing)
                    ? existing
                    : _connections[connectionId] = new HashSet<ulong>();

                if (guilds.Add(guildId))
                    _watchers[guildId] = _watchers.GetValueOrDefault(guildId) + 1;
            }
        }

        public void Remove(string connectionId, ulong guildId)
        {
            lock (_sync)
            {
                if (_connections.TryGetValue(connectionId, out var guilds) && guilds.Remove(guildId))
                    Release(guildId);

                // Watching is what the spectrum hangs off, so it cannot outlive it
                if (_spectrumConnections.TryGetValue(connectionId, out var open) && open.Remove(guildId))
                    ReleaseSpectrum(guildId);
            }
        }

        public void Drop(string connectionId)
        {
            lock (_sync)
            {
                if (_spectrumConnections.Remove(connectionId, out var open))
                {
                    foreach (var guildId in open)
                        ReleaseSpectrum(guildId);
                }

                if (!_connections.Remove(connectionId, out var guilds))
                    return;

                foreach (var guildId in guilds)
                    Release(guildId);
            }
        }

        private void Release(ulong guildId) => Release(_watchers, guildId);
        private void ReleaseSpectrum(ulong guildId) => Release(_spectrumWatchers, guildId);

        private static void Release(Dictionary<ulong, int> counts, ulong guildId)
        {
            var count = counts.GetValueOrDefault(guildId) - 1;

            if (count > 0)
                counts[guildId] = count;
            else
                counts.Remove(guildId);
        }
    }
}
