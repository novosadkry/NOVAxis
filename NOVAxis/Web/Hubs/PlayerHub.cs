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

        public IReadOnlyList<ulong> ActiveGuilds
        {
            get { lock (_sync) return _watchers.Keys.ToList(); }
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
            }
        }

        public void Drop(string connectionId)
        {
            lock (_sync)
            {
                if (!_connections.Remove(connectionId, out var guilds))
                    return;

                foreach (var guildId in guilds)
                    Release(guildId);
            }
        }

        private void Release(ulong guildId)
        {
            var count = _watchers.GetValueOrDefault(guildId) - 1;

            if (count > 0)
                _watchers[guildId] = count;
            else
                _watchers.Remove(guildId);
        }
    }
}
